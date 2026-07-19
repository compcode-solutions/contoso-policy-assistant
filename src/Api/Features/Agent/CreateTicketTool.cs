namespace Contoso.PolicyAssistant.Api.Features.Agent;

/// <summary>
/// Write tool — never executes without human approval (HITL).
/// Thin stand-in for a Semantic Kernel plugin / Azure Logic App action.
/// </summary>
public static class CreateTicketTool
{
    public const string Name = "create_ticket";
    public const bool RequiresApproval = true;

    public static TicketDraft Propose(string question, string requestedBy)
    {
        var severity = question.Contains("P1", StringComparison.OrdinalIgnoreCase)
            || question.Contains("Priority-1", StringComparison.OrdinalIgnoreCase)
            || question.Contains("safety", StringComparison.OrdinalIgnoreCase)
            ? "P1"
            : "P2";

        return new TicketDraft
        {
            Title = severity == "P1"
                ? "Safety escalation — Contoso site incident"
                : "Policy escalation ticket",
            Body =
                $"""
                Auto-drafted from Policy Assistant (requires human approval).
                Requested by: {requestedBy}
                User message: {question.Trim()}

                Per Workplace Safety Escalation SOP: ensure people are safe, notify Safety Officer within 1 hour, open Priority-1 with location/time/people/actions.
                """,
            Severity = severity
        };
    }
}

public sealed class TicketDraft
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required string Severity { get; init; }
}
