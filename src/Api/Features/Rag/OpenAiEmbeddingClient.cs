using OpenAI.Embeddings;

namespace Contoso.PolicyAssistant.Api.Features.Rag;

public sealed class OpenAiEmbeddingClient(EmbeddingClient client, string providerName, string modelName)
    : IEmbeddingClient
{
    /// <summary>Default width of text-embedding-3-small.</summary>
    public const int DefaultDimensions = 1536;

    public string ProviderName { get; } = providerName;
    public string ModelName { get; } = modelName;

    public async Task<EmbeddingCallResult> EmbedAsync(string text, CancellationToken ct = default)
    {
        var batch = await EmbedBatchAsync([text], ct);
        return new EmbeddingCallResult
        {
            Vector = batch.Vectors[0],
            TokenCount = batch.TokenCount,
            Model = ModelName
        };
    }

    public async Task<EmbeddingBatchResult> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default)
    {
        if (texts.Count == 0)
        {
            return new EmbeddingBatchResult { Vectors = [], TokenCount = 0, Model = ModelName };
        }

        var result = await client.GenerateEmbeddingsAsync(texts, cancellationToken: ct);
        var collection = result.Value;
        return new EmbeddingBatchResult
        {
            Vectors = collection.Select(e => e.ToFloats().ToArray()).ToList(),
            TokenCount = collection.Usage?.InputTokenCount ?? 0,
            Model = ModelName
        };
    }
}
