using Contoso.PolicyAssistant.Api.Features.Rag;

namespace Contoso.PolicyAssistant.Api.Features.Ask;

public sealed class AskQuestionResponse
{
    public required string Answer { get; init; }
    public required IReadOnlyList<Citation> Citations { get; init; }
    public required bool Grounded { get; init; }
    public required string Question { get; init; }
    public required DateTimeOffset ReceivedUtc { get; init; }
    public required string Phase { get; init; }
    public required string[] CallerRoles { get; init; }
    public required string Provider { get; init; }
    public string Model { get; init; } = "";
    public int ChunksRetrieved { get; init; }
    public int ChunksFilteredByRole { get; init; }
    public int CorpusCount { get; init; }
    public int VisibleBeforeScoring { get; init; }
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int EmbeddingTokens { get; init; }
    public int TotalTokens { get; init; }
    public int LatencyMs { get; init; }
    public bool Fallback { get; init; }
    public string? FallbackReason { get; init; }
    public string Note { get; init; } = string.Empty;
}
