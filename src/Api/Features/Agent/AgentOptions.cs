namespace Contoso.PolicyAssistant.Api.Features.Agent;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public int MaxSteps { get; set; } = 4;
    public int TimeoutSeconds { get; set; } = 30;
}
