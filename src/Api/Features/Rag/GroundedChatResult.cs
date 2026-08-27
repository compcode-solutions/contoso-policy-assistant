namespace Contoso.PolicyAssistant.Api.Features.Rag;

public sealed class GroundedChatResult
{
    public required string Text { get; init; }
    public string Model { get; init; } = "";
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens => PromptTokens + CompletionTokens;
}

public sealed class EmbeddingCallResult
{
    public required float[] Vector { get; init; }
    public int TokenCount { get; init; }
    public string Model { get; init; } = "";
    public int Dimensions => Vector.Length;
}

public sealed class EmbeddingBatchResult
{
    public required IReadOnlyList<float[]> Vectors { get; init; }
    public int TokenCount { get; init; }
    public string Model { get; init; } = "";
}
