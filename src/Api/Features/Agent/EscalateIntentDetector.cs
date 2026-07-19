using System.Text.RegularExpressions;

namespace Contoso.PolicyAssistant.Api.Features.Agent;

public static class EscalateIntentDetector
{
    private static readonly Regex Intent = new(
        @"\b(escalate|escalation|create\s+(a\s+)?ticket|priority-?1|p1\s+ticket|incident\s+ticket|safety\s+incident)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsEscalateIntent(string question) =>
        Intent.IsMatch(question);
}
