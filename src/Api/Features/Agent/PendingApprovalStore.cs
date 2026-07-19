namespace Contoso.PolicyAssistant.Api.Features.Agent;

public sealed class PendingApprovalStore
{
    private readonly Dictionary<Guid, PendingApproval> _items = new();
    private readonly object _gate = new();

    public PendingApproval Add(PendingApproval item)
    {
        lock (_gate)
        {
            _items[item.Id] = item;
            return item;
        }
    }

    public PendingApproval? Get(Guid id)
    {
        lock (_gate)
        {
            return _items.TryGetValue(id, out var item) ? item : null;
        }
    }

    public IReadOnlyList<PendingApproval> ListPending()
    {
        lock (_gate)
        {
            return _items.Values
                .Where(i => i.Status == "pending")
                .OrderByDescending(i => i.CreatedUtc)
                .ToList();
        }
    }
}
