namespace Contoso.PolicyAssistant.Api.Features.Rag;

public interface IGroundedChatClient
{
    string ProviderName { get; }
    string ModelName { get; }
    Task<GroundedChatResult> AnswerAsync(
        string question,
        IReadOnlyList<RetrievedChunk> context,
        CancellationToken ct = default);
}
