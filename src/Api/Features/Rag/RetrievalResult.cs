namespace Contoso.PolicyAssistant.Api.Features.Rag;

public sealed class RetrievalResult
{
    public IReadOnlyList<RetrievedChunk> Hits { get; init; } = [];

    /// <summary>Chunks in the index before any role filter.</summary>
    public int CorpusCount { get; init; }

    /// <summary>Chunks that survived the ACL filter, before cosine scoring.</summary>
    public int VisibleBeforeScoring { get; init; }

    /// <summary>
    /// CorpusCount − VisibleBeforeScoring. This is the number dropped by role
    /// BEFORE similarity — the figure the UI surfaces.
    /// </summary>
    public int FilteredByRole { get; init; }
}
