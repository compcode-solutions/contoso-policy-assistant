namespace Contoso.PolicyAssistant.Api.Features.Policies;

public sealed class PolicySummary
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string[] AllowedRoles { get; init; }
}
