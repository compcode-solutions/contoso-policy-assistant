namespace Contoso.PolicyAssistant.Api.Features.Ask;

/// <summary>
/// Fixed visitor questions with role-specific pre-computed answers.
/// Retrieval + ACL still run live; generation does not call a hosted LLM.
/// </summary>
public static class DemoScriptCatalog
{
    public const string FallbackReason = "demo-script";
    public const string ModelName = "demo-script";

    public const string VisitorBanner =
        "Recorded demo answer — retrieval and role filtering are live; the wording is pre-computed so this proof does not depend on the hosted model.";

    private static readonly DemoScriptEntry[] Entries =
    [
        new(
            Id: "leave-days",
            Aliases:
            [
                "how many leave days do i get each year",
                "how many days of annual leave do employees get",
                "how many leave days"
            ],
            Resolve: roles => new DemoScriptAnswer(
                Answer:
                    "Full-time employees receive 20 days of paid annual leave per calendar year. " +
                    "Leave requests should be submitted at least 5 business days in advance when possible [1].",
                Grounded: true,
                PreferCitationContains: "leave")),
        new(
            Id: "cafeteria-refuse",
            Aliases:
            [
                "whats the cafeteria menu for friday",
                "what is the cafeteria menu for friday",
                "cafeteria menu"
            ],
            Resolve: _ => new DemoScriptAnswer(
                Answer: "I don't know based on the policies I can access.",
                Grounded: false,
                PreferCitationContains: null)),
        new(
            Id: "safety-acl",
            Aliases:
            [
                "what should a supervisor do after a safety incident",
                "what should a supervisor do after a safety incident mention safety officer and priority-1",
                "what is the workplace safety escalation priority-1 ticket process",
                "what is the workplace safety escalation sop priority-1 ticket process"
            ],
            Resolve: roles =>
            {
                if (HasRole(roles, "Supervisor") || HasRole(roles, "Admin"))
                {
                    return new DemoScriptAnswer(
                        Answer:
                            "After a safety incident, ensure people are safe, notify the site Safety Officer within 1 hour, " +
                            "and escalate by creating a Priority-1 ticket in the incident system with location, time, people involved, " +
                            "and immediate actions. Do not discuss details on public channels [1].",
                        Grounded: true,
                        PreferCitationContains: "safety");
                }

                return new DemoScriptAnswer(
                    Answer: "I don't know based on the policies I can access.",
                    Grounded: false,
                    PreferCitationContains: null);
            })
    ];

    public static DemoScriptAnswer? TryResolve(string question, IEnumerable<string> roles)
    {
        var key = Normalize(question);
        if (string.IsNullOrEmpty(key)) return null;

        foreach (var entry in Entries)
        {
            foreach (var alias in entry.Aliases)
            {
                var a = Normalize(alias);
                if (key == a || key.StartsWith(a, StringComparison.Ordinal) || a.StartsWith(key, StringComparison.Ordinal))
                {
                    return entry.Resolve(roles.ToArray());
                }
            }
        }

        return null;
    }

    public static string Normalize(string question)
    {
        if (string.IsNullOrWhiteSpace(question)) return "";
        var chars = question.Trim().ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c is '%' or '$')
            .ToArray();
        var collapsed = new string(chars);
        return string.Join(' ', collapsed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool HasRole(IEnumerable<string> roles, string role) =>
        roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

    private sealed record DemoScriptEntry(
        string Id,
        string[] Aliases,
        Func<string[], DemoScriptAnswer> Resolve);
}

public sealed record DemoScriptAnswer(
    string Answer,
    bool Grounded,
    string? PreferCitationContains);
