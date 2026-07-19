namespace Contoso.PolicyAssistant.Api.Features.Agent;

public sealed class TicketRecord
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required string Severity { get; init; }
    public required string CreatedBy { get; init; }
    public required string ApprovedBy { get; init; }
    public required Guid ApprovalId { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed class TicketStore
{
    private readonly List<TicketRecord> _tickets = [];
    private readonly object _gate = new();

    public TicketRecord Create(TicketDraft draft, string createdBy, string approvedBy, Guid approvalId)
    {
        var ticket = new TicketRecord
        {
            Id = Guid.NewGuid(),
            Title = draft.Title,
            Body = draft.Body,
            Severity = draft.Severity,
            CreatedBy = createdBy,
            ApprovedBy = approvedBy,
            ApprovalId = approvalId,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        lock (_gate)
        {
            _tickets.Add(ticket);
        }

        return ticket;
    }

    public IReadOnlyList<TicketRecord> List()
    {
        lock (_gate)
        {
            return _tickets.OrderByDescending(t => t.CreatedUtc).ToList();
        }
    }
}
