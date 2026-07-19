using OpenAI.Embeddings;

namespace Contoso.PolicyAssistant.Api.Features.Rag;

public sealed class OpenAiEmbeddingClient(EmbeddingClient client, string providerName) : IEmbeddingClient
{
    public string ProviderName { get; } = providerName;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var result = await client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Value.ToFloats().ToArray();
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];

        var result = await client.GenerateEmbeddingsAsync(texts, cancellationToken: ct);
        return result.Value.Select(e => e.ToFloats().ToArray()).ToList();
    }
}
