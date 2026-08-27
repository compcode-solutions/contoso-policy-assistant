using Contoso.PolicyAssistant.Api.Features.Policies;

namespace Contoso.PolicyAssistant.Api.Features.Rag;

public sealed class IngestService(
    PolicyCatalog catalog,
    InMemoryVectorStore store,
    IEmbeddingClient embeddings,
    ILogger<IngestService> logger)
{
    public async Task<IngestResult> IngestAsync(CancellationToken ct = default)
    {
        var built = new List<PolicyChunk>();
        var texts = new List<string>();
        var meta = new List<(string DocId, string Title, string FileName, string[] Roles, int Ordinal, string Text)>();

        foreach (var doc in catalog.All)
        {
            var pieces = TextChunker.Chunk(doc.BodyMarkdown);
            var ordinal = 0;
            foreach (var piece in pieces)
            {
                meta.Add((doc.Id, doc.Title, doc.FileName, doc.AllowedRoles, ordinal, piece));
                texts.Add(piece);
                ordinal++;
            }
        }

        logger.LogInformation(
            "Ingesting {DocCount} policies → {ChunkCount} chunks via {Provider}/{Model}",
            catalog.All.Count,
            texts.Count,
            embeddings.ProviderName,
            embeddings.ModelName);

        var vectors = await embeddings.EmbedBatchAsync(texts, ct);
        // Lexical vectors are always stored so a hosted outage can fall back without mixing spaces.
        var lexical = texts.Select(LexicalEmbeddingClient.Embed).ToList();

        for (var i = 0; i < meta.Count; i++)
        {
            var m = meta[i];
            built.Add(new PolicyChunk
            {
                Id = $"{m.DocId}:{m.Ordinal}",
                DocumentId = m.DocId,
                Title = m.Title,
                FileName = m.FileName,
                AllowedRoles = m.Roles,
                Text = m.Text,
                Embedding = vectors.Vectors[i],
                LexicalEmbedding = lexical[i],
                Ordinal = m.Ordinal
            });
        }

        store.ReplaceAll(built, embeddings.ProviderName);

        logger.LogInformation(
            "Ingest embeddings tokens={Tokens} model={Model} dims={Dims}",
            vectors.TokenCount,
            vectors.Model,
            built.Count == 0 ? 0 : built[0].Embedding.Length);

        return new IngestResult
        {
            DocumentCount = catalog.All.Count,
            ChunkCount = built.Count,
            Provider = embeddings.ProviderName,
            EmbeddingModel = embeddings.ModelName,
            EmbeddingTokens = vectors.TokenCount,
            IngestedUtc = store.IngestedUtc ?? DateTimeOffset.UtcNow
        };
    }
}

public sealed class IngestResult
{
    public required int DocumentCount { get; init; }
    public required int ChunkCount { get; init; }
    public required string Provider { get; init; }
    public string EmbeddingModel { get; init; } = "";
    public int EmbeddingTokens { get; init; }
    public required DateTimeOffset IngestedUtc { get; init; }
}
