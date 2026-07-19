namespace Contoso.PolicyAssistant.Api.Features.Rag;

public interface IGroundedChatClient
{
    string ProviderName { get; }
    Task<string> AnswerAsync(string question, IReadOnlyList<RetrievedChunk> context, CancellationToken ct = default);
}
