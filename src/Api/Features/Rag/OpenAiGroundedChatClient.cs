using System.Text;
using OpenAI.Chat;

namespace Contoso.PolicyAssistant.Api.Features.Rag;

public sealed class OpenAiGroundedChatClient(ChatClient client, string providerName) : IGroundedChatClient
{
    public string ProviderName { get; } = providerName;

    public async Task<string> AnswerAsync(
        string question,
        IReadOnlyList<RetrievedChunk> context,
        CancellationToken ct = default)
    {
        if (context.Count == 0)
        {
            return "I don't know based on the policies I can access.";
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
        return completion.Value.Content[0].Text.Trim();
    }
}
