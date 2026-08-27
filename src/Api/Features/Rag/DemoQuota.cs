namespace Contoso.PolicyAssistant.Api.Features.Rag;

/// <summary>
/// Process-wide UTC daily ceiling for hosted model calls. Lexical retrieval is free
/// and does not consume this quota.
/// </summary>
public sealed class DemoQuota
{
    private readonly object _gate = new();
    private DateTime _utcDay;
    private int _hostedCount;

    public DemoQuota(int dailyCeiling)
    {
        DailyCeiling = dailyCeiling < 1 ? 1 : dailyCeiling;
        _utcDay = DateTime.UtcNow.Date;
    }

    public int DailyCeiling { get; }

    public int HostedCountToday
    {
        get { lock (_gate) { RollDay(); return _hostedCount; } }
    }

    public int RemainingToday
    {
        get { lock (_gate) { RollDay(); return Math.Max(0, DailyCeiling - _hostedCount); } }
    }

    public bool CapReached
    {
        get { lock (_gate) { RollDay(); return _hostedCount >= DailyCeiling; } }
    }

    /// <summary>Reserve one hosted call. Returns false when the daily ceiling is already hit.</summary>
    public bool TryConsumeHosted()
    {
        lock (_gate)
        {
            RollDay();
            if (_hostedCount >= DailyCeiling) return false;
            _hostedCount++;
            return true;
        }
    }

    private void RollDay()
    {
        var today = DateTime.UtcNow.Date;
        if (today == _utcDay) return;
        _utcDay = today;
        _hostedCount = 0;
    }
}
