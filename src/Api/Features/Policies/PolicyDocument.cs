namespace Contoso.PolicyAssistant.Api.Features.Policies;

public sealed class PolicyDocument
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string[] AllowedRoles { get; init; }
    public required string FileName { get; init; }
    public required string BodyMarkdown { get; init; }

    public bool IsVisibleTo(IEnumerable<string> userRoles) =>
        AllowedRoles.Any(ar =>
            userRoles.Any(ur => string.Equals(ar, ur, StringComparison.OrdinalIgnoreCase)));
}
