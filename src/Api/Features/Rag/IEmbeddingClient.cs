namespace Contoso.PolicyAssistant.Api.Features.Rag;

public interface IEmbeddingClient
{
    string ProviderName { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
