namespace Contoso.PolicyAssistant.Api.Features.Agent;

public sealed class PendingApproval
{
    public required Guid Id { get; init; }
    public required string Tool { get; init; }
    public required bool RequiresApproval { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required string Severity { get; init; }
    public required string RequestedBy { get; init; }
    public required string[] RequestedByRoles { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public string Status { get; set; } = "pending"; // pending | approved | rejected
    public DateTimeOffset? ResolvedUtc { get; set; }
    public string? ResolvedBy { get; set; }
    public Guid? TicketId { get; set; }
}
