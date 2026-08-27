namespace Contoso.PolicyAssistant.Api.Features.Rag;

public sealed class PolicyChunk
{
    public required string Id { get; init; }
    public required string DocumentId { get; init; }
    public required string Title { get; init; }
    public required string FileName { get; init; }
    public required string[] AllowedRoles { get; init; }
    public required string Text { get; init; }
    public required float[] Embedding { get; init; }
    /// <summary>
    /// Always populated at ingest so a hosted-provider failure can fall back to
    /// lexical search without mixing vector spaces.
    /// </summary>
    public float[] LexicalEmbedding { get; init; } = [];
    public int Ordinal { get; init; }

    public bool IsVisibleTo(IEnumerable<string> userRoles) =>
        AllowedRoles.Any(ar =>
            userRoles.Any(ur => string.Equals(ar, ur, StringComparison.OrdinalIgnoreCase)));
}
