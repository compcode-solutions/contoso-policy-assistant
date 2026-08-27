namespace Contoso.PolicyAssistant.Api.Features.Rag;

public sealed class InMemoryVectorStore
{
    private readonly object _gate = new();
    private List<PolicyChunk> _chunks = [];

    public int Count
    {
        get { lock (_gate) return _chunks.Count; }
    }

    public string Provider { get; set; } = "none";
    public DateTimeOffset? IngestedUtc { get; private set; }

    public void ReplaceAll(IEnumerable<PolicyChunk> chunks, string provider)
    {
        lock (_gate)
        {
            _chunks = chunks.ToList();
            Provider = provider;
            IngestedUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// ACL filter runs on the snapshot BEFORE cosine similarity. Do not reorder
    /// these two steps — that is the security property this demo exists to show.
    /// </summary>
    public RetrievalResult Search(
        float[] queryEmbedding,
        IEnumerable<string> userRoles,
        int topK,
        float minScore,
        bool useLexicalVectors = false)
    {
        var roles = userRoles.ToArray();
        List<PolicyChunk> corpus;
        lock (_gate)
        {
            corpus = _chunks.ToList();
        }

        // ACL FIRST — restricted chunks never enter the candidate list.
        var snapshot = corpus.Where(c => c.IsVisibleTo(roles)).ToList();
        var filteredByRole = corpus.Count - snapshot.Count;

        var hits = snapshot
            .Select(c => new
            {
                Chunk = c,
                Score = VectorMath.CosineSimilarity(queryEmbedding, VectorFor(c, useLexicalVectors))
            })
            .Where(x => x.Score >= minScore)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select((x, i) => new RetrievedChunk
            {
                N = i + 1,
                Chunk = x.Chunk,
                Score = x.Score
            })
            .ToList();

        return new RetrievalResult
        {
            Hits = hits,
            CorpusCount = corpus.Count,
            VisibleBeforeScoring = snapshot.Count,
            FilteredByRole = filteredByRole
        };
    }

    private static float[] VectorFor(PolicyChunk chunk, bool useLexical)
    {
        if (useLexical && chunk.LexicalEmbedding is { Length: > 0 })
            return chunk.LexicalEmbedding;
        return chunk.Embedding;
    }
}
