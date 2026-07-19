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

    public IReadOnlyList<RetrievedChunk> Search(
        float[] queryEmbedding,
        IEnumerable<string> userRoles,
        int topK,
        float minScore)
    {
        var roles = userRoles.ToArray();
        List<PolicyChunk> snapshot;
        lock (_gate)
        {
            snapshot = _chunks.Where(c => c.IsVisibleTo(roles)).ToList();
        }

        return snapshot
            .Select(c => new
            {
                Chunk = c,
                Score = VectorMath.CosineSimilarity(queryEmbedding, c.Embedding)
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
    }
}
