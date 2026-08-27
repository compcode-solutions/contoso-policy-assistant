namespace Contoso.PolicyAssistant.Api.Features.Rag;

public interface IEmbeddingClient
{
    string ProviderName { get; }
    string ModelName { get; }
    Task<EmbeddingCallResult> EmbedAsync(string text, CancellationToken ct = default);
    Task<EmbeddingBatchResult> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
