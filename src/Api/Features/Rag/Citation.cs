namespace Contoso.PolicyAssistant.Api.Features.Rag;

public sealed class Citation
{
    public required int N { get; init; }
    public required string Title { get; init; }
    public required string Excerpt { get; init; }
}
