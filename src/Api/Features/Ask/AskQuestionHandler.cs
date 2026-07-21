using System.Diagnostics;
using Contoso.PolicyAssistant.Api.Features.Logging;
using Contoso.PolicyAssistant.Api.Features.Rag;
using Microsoft.Extensions.Options;

namespace Contoso.PolicyAssistant.Api.Features.Ask;

public sealed class AskQuestionHandler(
    InMemoryVectorStore store,
    IEmbeddingClient embeddings,
    IGroundedChatClient chat,
    IAiCallLogger aiLog,
    IOptions<RagOptions> ragOptions)
{
    private static readonly string Refusal =
        "I don't know based on the policies I can access.";

    public async Task<AskQuestionResponse> HandleAsync(
        AskQuestionRequest request,
        IEnumerable<string> roles,
        CancellationToken ct = default)
    {
        var question = request.Question.Trim();
        var roleList = roles.ToArray();
        var opts = ragOptions.Value;
        var sw = Stopwatch.StartNew();

        if (store.Count == 0)
        {
            return Refuse(question, roleList, "Index empty — call POST /api/ingest first.");
        }

        var queryVec = await embeddings.EmbedAsync(question, ct);
        var hits = store.Search(queryVec, roleList, opts.TopK, opts.MinScore);

        if (hits.Count == 0)
        {
            aiLog.Log(new AiCallRecord
            {
                Operation = "rag.retrieve",
                Provider = embeddings.ProviderName,
                InputPreview = question,
                OutputPreview = "0 hits",
                DurationMs = (int)sw.ElapsedMilliseconds,
                Meta = new Dictionary<string, string> { ["roles"] = string.Join(',', roleList) }
            });
            return Refuse(
                question,
                roleList,
                "No role-visible chunks scored high enough. ACL filter runs at retrieve time.");
        }

        var answer = await chat.AnswerAsync(question, hits, ct);
        var grounded = IsGrounded(answer);

        aiLog.Log(new AiCallRecord
        {
            Operation = "rag.answer",
            Provider = $"{embeddings.ProviderName}/{chat.ProviderName}",
            InputPreview = question,
            OutputPreview = answer,
            DurationMs = (int)sw.ElapsedMilliseconds,
            Meta = new Dictionary<string, string>
            {
                ["grounded"] = grounded.ToString(),
                ["hits"] = hits.Count.ToString(),
                ["roles"] = string.Join(',', roleList)
            }
        });

        var citations = grounded
            ? hits.Select(h => new Citation
            {
                N = h.N,
                Title = h.Chunk.FileName,
                Excerpt = Excerpt(h.Chunk.Text, 180)
            }).ToList()
            : [];

        return new AskQuestionResponse
        {
            Answer = answer,
            Citations = citations,
            Grounded = grounded,
            Question = question,
            ReceivedUtc = DateTimeOffset.UtcNow,
            Phase = "grounded-rag",
            CallerRoles = roleList,
            Provider = $"{embeddings.ProviderName}/{chat.ProviderName}",
            Note = grounded
                ? "Answer constrained to retrieved, role-filtered policy chunks."
                : "Model refused or could not ground — citations omitted."
        };
    }

    private AskQuestionResponse Refuse(string question, string[] roles, string note) =>
        new()
        {
            Answer = Refusal,
            Citations = [],
            Grounded = false,
            Question = question,
            ReceivedUtc = DateTimeOffset.UtcNow,
            Phase = "grounded-rag",
            CallerRoles = roles,
            Provider = $"{embeddings.ProviderName}/{chat.ProviderName}",
            Note = note
        };

    private static bool IsGrounded(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return false;
        var a = answer.Trim();
        if (a.StartsWith("I don't know", StringComparison.OrdinalIgnoreCase)) return false;
        if (a.Contains("don't know based on", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static string Excerpt(string text, int max)
    {
        var oneLine = text.Replace('\n', ' ').Trim();
        return oneLine.Length <= max ? oneLine : oneLine[..max].TrimEnd() + "…";
    }
}
