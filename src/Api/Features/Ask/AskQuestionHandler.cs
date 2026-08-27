using System.ClientModel;
using System.Diagnostics;
using Contoso.PolicyAssistant.Api.Features.Logging;
using Contoso.PolicyAssistant.Api.Features.Rag;
using Microsoft.Extensions.Options;

namespace Contoso.PolicyAssistant.Api.Features.Ask;

public sealed class AskQuestionHandler
{
    private static readonly string Refusal =
        "I don't know based on the policies I can access.";

    private readonly InMemoryVectorStore _store;
    private readonly IEmbeddingClient _hostedEmbeddings;
    private readonly IGroundedChatClient _hostedChat;
    private readonly IEmbeddingClient _lexicalEmbeddings;
    private readonly IGroundedChatClient _lexicalChat;
    private readonly IAiCallLogger _aiLog;
    private readonly RagOptions _rag;
    private readonly DemoQuota _quota;
    private readonly bool _hostedConfigured;

    public AskQuestionHandler(
        InMemoryVectorStore store,
        IEmbeddingClient embeddings,
        IGroundedChatClient chat,
        IAiCallLogger aiLog,
        IOptions<RagOptions> ragOptions)
        : this(
            store,
            embeddings,
            chat,
            aiLog,
            ragOptions,
            new DemoQuota(int.MaxValue),
            new LexicalEmbeddingClient(),
            new LexicalGroundedChatClient(),
            hostedConfigured: false)
    {
    }

    public AskQuestionHandler(
        InMemoryVectorStore store,
        IEmbeddingClient embeddings,
        IGroundedChatClient chat,
        IAiCallLogger aiLog,
        IOptions<RagOptions> ragOptions,
        DemoQuota quota,
        IEmbeddingClient lexicalEmbeddings,
        IGroundedChatClient lexicalChat,
        bool hostedConfigured)
    {
        _store = store;
        _hostedEmbeddings = embeddings;
        _hostedChat = chat;
        _aiLog = aiLog;
        _rag = ragOptions.Value;
        _quota = quota;
        _lexicalEmbeddings = lexicalEmbeddings;
        _lexicalChat = lexicalChat;
        _hostedConfigured = hostedConfigured;
    }

    public async Task<AskQuestionResponse> HandleAsync(
        AskQuestionRequest request,
        IEnumerable<string> roles,
        CancellationToken ct = default)
    {
        var question = request.Question.Trim();
        var roleList = roles.ToArray();
        var sw = Stopwatch.StartNew();

        if (_store.Count == 0)
        {
            return Refuse(question, roleList, "Index empty — call POST /api/ingest first.", sw);
        }

        if (_hostedConfigured && IsHostedIndex())
        {
            if (!_quota.TryConsumeHosted())
            {
                return await AnswerWithAsync(
                    question,
                    roleList,
                    _lexicalEmbeddings,
                    _lexicalChat,
                    useLexicalVectors: true,
                    fallback: true,
                    fallbackReason: "daily-ceiling",
                    sw,
                    ct);
            }

            try
            {
                return await AnswerWithAsync(
                    question,
                    roleList,
                    _hostedEmbeddings,
                    _hostedChat,
                    useLexicalVectors: false,
                    fallback: false,
                    fallbackReason: null,
                    sw,
                    ct);
            }
            catch (Exception ex) when (IsProviderFailure(ex, ct))
            {
                _aiLog.Log(new AiCallRecord
                {
                    Operation = "rag.fallback",
                    Provider = _hostedEmbeddings.ProviderName,
                    InputPreview = question,
                    OutputPreview = ex.GetType().Name,
                    DurationMs = (int)sw.ElapsedMilliseconds,
                    Meta = new Dictionary<string, string>
                    {
                        ["reason"] = "provider-error",
                        ["roles"] = string.Join(',', roleList)
                    }
                });

                return await AnswerWithAsync(
                    question,
                    roleList,
                    _lexicalEmbeddings,
                    _lexicalChat,
                    useLexicalVectors: true,
                    fallback: true,
                    fallbackReason: "provider-error",
                    sw,
                    ct);
            }
        }

        return await AnswerWithAsync(
            question,
            roleList,
            _hostedEmbeddings,
            _hostedChat,
            useLexicalVectors: false,
            fallback: false,
            fallbackReason: null,
            sw,
            ct);
    }

    private async Task<AskQuestionResponse> AnswerWithAsync(
        string question,
        string[] roleList,
        IEmbeddingClient embeddings,
        IGroundedChatClient chat,
        bool useLexicalVectors,
        bool fallback,
        string? fallbackReason,
        Stopwatch sw,
        CancellationToken ct)
    {
        var query = await embeddings.EmbedAsync(question, ct);
        var retrieval = _store.Search(
            query.Vector,
            roleList,
            _rag.TopK,
            _rag.MinScore,
            useLexicalVectors);

        if (retrieval.Hits.Count == 0)
        {
            _aiLog.Log(new AiCallRecord
            {
                Operation = "rag.retrieve",
                Provider = embeddings.ProviderName,
                InputPreview = question,
                OutputPreview = "0 hits",
                DurationMs = (int)sw.ElapsedMilliseconds,
                EmbeddingTokens = query.TokenCount,
                Meta = RetrievalMeta(retrieval, roleList, fallback, fallbackReason)
            });
            return Refuse(
                question,
                roleList,
                fallback
                    ? FallbackNote(fallbackReason) + " No role-visible chunks scored high enough. ACL filter runs at retrieve time."
                    : "No role-visible chunks scored high enough. ACL filter runs at retrieve time.",
                sw,
                embeddings,
                chat,
                retrieval,
                query.TokenCount,
                fallback,
                fallbackReason);
        }

        var answer = await chat.AnswerAsync(question, retrieval.Hits, ct);
        var grounded = IsGrounded(answer.Text);
        var totalTokens = query.TokenCount + answer.TotalTokens;

        _aiLog.Log(new AiCallRecord
        {
            Operation = "rag.answer",
            Provider = $"{embeddings.ProviderName}/{chat.ProviderName}",
            InputPreview = question,
            OutputPreview = answer.Text,
            DurationMs = (int)sw.ElapsedMilliseconds,
            PromptTokens = answer.PromptTokens,
            CompletionTokens = answer.CompletionTokens,
            EmbeddingTokens = query.TokenCount,
            TotalTokens = totalTokens,
            Meta = RetrievalMeta(retrieval, roleList, fallback, fallbackReason, grounded)
        });

        var citations = grounded
            ? retrieval.Hits.Select(h => new Citation
            {
                N = h.N,
                Title = h.Chunk.FileName,
                Excerpt = Excerpt(h.Chunk.Text, 180)
            }).ToList()
            : [];

        return new AskQuestionResponse
        {
            Answer = answer.Text,
            Citations = citations,
            Grounded = grounded,
            Question = question,
            ReceivedUtc = DateTimeOffset.UtcNow,
            Phase = "grounded-rag",
            CallerRoles = roleList,
            Provider = $"{embeddings.ProviderName}/{chat.ProviderName}",
            Model = answer.Model,
            ChunksRetrieved = retrieval.Hits.Count,
            ChunksFilteredByRole = retrieval.FilteredByRole,
            CorpusCount = retrieval.CorpusCount,
            VisibleBeforeScoring = retrieval.VisibleBeforeScoring,
            PromptTokens = answer.PromptTokens,
            CompletionTokens = answer.CompletionTokens,
            EmbeddingTokens = query.TokenCount,
            TotalTokens = totalTokens,
            LatencyMs = (int)sw.ElapsedMilliseconds,
            Fallback = fallback,
            FallbackReason = fallbackReason,
            Note = NoteFor(grounded, fallback, fallbackReason)
        };
    }

    private AskQuestionResponse Refuse(
        string question,
        string[] roles,
        string note,
        Stopwatch sw,
        IEmbeddingClient? embeddings = null,
        IGroundedChatClient? chat = null,
        RetrievalResult? retrieval = null,
        int embeddingTokens = 0,
        bool fallback = false,
        string? fallbackReason = null)
    {
        embeddings ??= _hostedEmbeddings;
        chat ??= _hostedChat;
        return new AskQuestionResponse
        {
            Answer = Refusal,
            Citations = [],
            Grounded = false,
            Question = question,
            ReceivedUtc = DateTimeOffset.UtcNow,
            Phase = "grounded-rag",
            CallerRoles = roles,
            Provider = $"{embeddings.ProviderName}/{chat.ProviderName}",
            Model = chat.ModelName,
            ChunksRetrieved = retrieval?.Hits.Count ?? 0,
            ChunksFilteredByRole = retrieval?.FilteredByRole ?? 0,
            CorpusCount = retrieval?.CorpusCount ?? _store.Count,
            VisibleBeforeScoring = retrieval?.VisibleBeforeScoring ?? 0,
            EmbeddingTokens = embeddingTokens,
            TotalTokens = embeddingTokens,
            LatencyMs = (int)sw.ElapsedMilliseconds,
            Fallback = fallback,
            FallbackReason = fallbackReason,
            Note = note
        };
    }

    private static Dictionary<string, string> RetrievalMeta(
        RetrievalResult retrieval,
        string[] roles,
        bool fallback,
        string? fallbackReason,
        bool? grounded = null)
    {
        var meta = new Dictionary<string, string>
        {
            ["roles"] = string.Join(',', roles),
            ["hits"] = retrieval.Hits.Count.ToString(),
            ["filteredByRole"] = retrieval.FilteredByRole.ToString(),
            ["visibleBeforeScoring"] = retrieval.VisibleBeforeScoring.ToString(),
            ["corpus"] = retrieval.CorpusCount.ToString(),
            ["fallback"] = fallback.ToString()
        };
        if (fallbackReason is not null) meta["fallbackReason"] = fallbackReason;
        if (grounded is not null) meta["grounded"] = grounded.Value.ToString();
        return meta;
    }

    private static string NoteFor(bool grounded, bool fallback, string? fallbackReason)
    {
        var grounding = grounded
            ? "Answer constrained to retrieved, role-filtered policy chunks."
            : "Model refused or could not ground — citations omitted.";
        if (!fallback) return grounding;
        return FallbackNote(fallbackReason) + " " + grounding;
    }

    private static string FallbackNote(string? reason) =>
        reason == "daily-ceiling"
            ? "Demo quota reached for today — running on local lexical retrieval."
            : "Hosted model unavailable — running on local lexical retrieval.";

    private static bool IsProviderFailure(Exception ex, CancellationToken ct)
    {
        if (ex is OperationCanceledException)
            return !ct.IsCancellationRequested;
        if (ex is DemoQuotaExceededException) return true;
        if (ex is HttpRequestException) return true;
        if (ex is TimeoutException) return true;
        if (ex is ClientResultException) return true;
        return false;
    }

    private static bool IsGrounded(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return false;
        var a = answer.Trim();
        if (a.StartsWith("I don't know", StringComparison.OrdinalIgnoreCase)) return false;
        if (a.Contains("don't know based on", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private bool IsHostedIndex()
    {
        var p = _store.Provider;
        return string.Equals(p, "Gemini", StringComparison.OrdinalIgnoreCase)
            || string.Equals(p, "OpenAI", StringComparison.OrdinalIgnoreCase)
            || string.Equals(p, "AzureOpenAI", StringComparison.OrdinalIgnoreCase);
    }

    private static string Excerpt(string text, int max)
    {
        var oneLine = text.Replace('\n', ' ').Trim();
        return oneLine.Length <= max ? oneLine : oneLine[..max].TrimEnd() + "…";
    }
}
