namespace Contoso.PolicyAssistant.Api.Features.Rag;

public sealed class RetrievedChunk
{
    public required int N { get; init; }
    public required PolicyChunk Chunk { get; init; }
    public required float Score { get; init; }
}
