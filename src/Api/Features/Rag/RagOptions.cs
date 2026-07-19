namespace Contoso.PolicyAssistant.Api.Features.Rag;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    public int TopK { get; set; } = 4;
    public float MinScore { get; set; } = 0.12f;
    public bool AutoIngestOnStartup { get; set; } = true;
}
