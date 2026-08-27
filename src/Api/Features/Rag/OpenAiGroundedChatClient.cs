using System.Text;
using OpenAI.Chat;

namespace Contoso.PolicyAssistant.Api.Features.Rag;

public sealed class OpenAiGroundedChatClient(ChatClient client, string providerName, string modelName)
    : IGroundedChatClient
{
    public string ProviderName { get; } = providerName;
    public string ModelName { get; } = modelName;

    public async Task<GroundedChatResult> AnswerAsync(
        string question,
        IReadOnlyList<RetrievedChunk> context,
        CancellationToken ct = default)
    {
        if (context.Count == 0)
        {
            return new GroundedChatResult
            {
                Text = "I don't know based on the policies I can access.",
                Model = ModelName
            };
        }

        var sb = new StringBuilder();
        foreach (var hit in context)
        {
            sb.AppendLine($"[{hit.N}] Source: {hit.Chunk.FileName} ({hit.Chunk.Title})");
            sb.AppendLine(hit.Chunk.Text);
            sb.AppendLine();
        }

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                """
                You are Contoso Policy Assistant. Answer ONLY using the CONTEXT blocks.
                Rules:
                - If CONTEXT is insufficient, say exactly: I don't know based on the available policies.
                - Cite sources inline like [1] or [2] matching CONTEXT numbers.
                - Never invent policies, menus, or unrelated facts.
                - Be concise (2–5 sentences).
                """),
            new UserChatMessage(
                $"""
                CONTEXT:
                {sb}

                QUESTION:
                {question}
                """)
        };

        var completion = await client.CompleteChatAsync(messages, cancellationToken: ct);
        var value = completion.Value;
        var usage = value.Usage;
        return new GroundedChatResult
        {
            Text = value.Content[0].Text.Trim(),
            Model = string.IsNullOrWhiteSpace(value.Model) ? ModelName : value.Model,
            PromptTokens = usage?.InputTokenCount ?? 0,
            CompletionTokens = usage?.OutputTokenCount ?? 0
        };
    }
}
