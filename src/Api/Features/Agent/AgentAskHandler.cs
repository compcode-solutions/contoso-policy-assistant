using System.Diagnostics;
using Contoso.PolicyAssistant.Api.Features.Ask;
using Contoso.PolicyAssistant.Api.Features.Logging;
using Contoso.PolicyAssistant.Api.Features.Rag;
using Microsoft.Extensions.Options;

namespace Contoso.PolicyAssistant.Api.Features.Agent;

public sealed class AgentAskHandler(
    AskQuestionHandler ask,
    PendingApprovalStore pending,
    IAiCallLogger aiLog,
    IOptions<AgentOptions> options)
{
    public async Task<AgentAskResponse> HandleAsync(
        AskQuestionRequest request,
        string username,
        IEnumerable<string> roles,
        CancellationToken ct = default)
    {
        var opts = options.Value;
        var roleList = roles.ToArray();
        var question = request.Question.Trim();
        var steps = 0;
        var sw = Stopwatch.StartNew();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, opts.TimeoutSeconds)));
        var token = timeout.Token;

        // Step 1 — intent
        steps++;
        var escalate = EscalateIntentDetector.IsEscalateIntent(question);
        aiLog.Log(new AiCallRecord
        {
            Operation = "agent.intent",
            Provider = "rules",
            User = username,
            InputPreview = question,
            OutputPreview = escalate ? "escalate" : "rag",
            Meta = new Dictionary<string, string> { ["step"] = steps.ToString() }
        });

        if (escalate)
        {
            if (steps >= opts.MaxSteps)
            {
                return Stopped(question, roleList, steps, "Max agent steps reached.");
            }

            var canPropose = roleList.Any(r =>
                r is "Supervisor" or "Admin");

            if (!canPropose)
            {
                steps++;
                return new AgentAskResponse
                {
                    Status = "forbiddenTool",
                    Answer =
                        "I can explain policies, but only Supervisors/Admins may propose a create_ticket escalation. Please contact your supervisor — no ticket was created.",
                    Citations = [],
                    Grounded = false,
                    Question = question,
                    CallerRoles = roleList,
                    StepsUsed = steps,
                    Phase = "agent-hitl",
                    Note = "Write tool blocked by role. No side effects."
                };
            }

            // Step 2 — propose tool (never execute)
            steps++;
            var draft = CreateTicketTool.Propose(question, username);
            var item = pending.Add(new PendingApproval
            {
                Id = Guid.NewGuid(),
                Tool = CreateTicketTool.Name,
                RequiresApproval = CreateTicketTool.RequiresApproval,
                Title = draft.Title,
                Body = draft.Body,
                Severity = draft.Severity,
                RequestedBy = username,
                RequestedByRoles = roleList,
                CreatedUtc = DateTimeOffset.UtcNow
            });

            aiLog.Log(new AiCallRecord
            {
                Operation = "agent.tool.propose",
                Provider = CreateTicketTool.Name,
                User = username,
                InputPreview = question,
                OutputPreview = $"pending:{item.Id}; severity={item.Severity}",
                DurationMs = (int)sw.ElapsedMilliseconds,
                Meta = new Dictionary<string, string>
                {
                    ["requiresApproval"] = "true",
                    ["step"] = steps.ToString()
                }
            });

            // Optional step 3 — ground message with RAG context about SOP
            string sopHint = "";
            IReadOnlyList<Citation> citations = [];
            bool grounded = false;
            if (steps < opts.MaxSteps)
            {
                steps++;
                try
                {
                    var rag = await ask.HandleAsync(request, roleList, token);
                    sopHint = rag.Answer;
                    citations = rag.Citations;
                    grounded = rag.Grounded;
                }
                catch (OperationCanceledException)
                {
                    return Stopped(question, roleList, steps, "Agent timed out while retrieving policy context.");
                }
            }

            var answer =
                "I've prepared a create_ticket draft that requires human approval before anything is written. " +
                "Review the proposal below — Approve to create the ticket, or Reject to discard. " +
                (string.IsNullOrWhiteSpace(sopHint) ? "" : $"Policy context: {sopHint}");

            return new AgentAskResponse
            {
                Status = "pendingApproval",
                Answer = answer.Trim(),
                Citations = citations,
                Grounded = grounded,
                Question = question,
                CallerRoles = roleList,
                StepsUsed = steps,
                Phase = "agent-hitl",
                PendingApproval = ToDto(item),
                Note = "Tool proposed only — zero tickets written until Approve."
            };
        }

        // Non-tool path — grounded RAG
        if (steps >= opts.MaxSteps)
        {
            return Stopped(question, roleList, steps, "Max agent steps reached.");
        }

        steps++;
        try
        {
            var rag = await ask.HandleAsync(request, roleList, token);
            return new AgentAskResponse
            {
                Status = "answered",
                Answer = rag.Answer,
                Citations = rag.Citations,
                Grounded = rag.Grounded,
                Question = question,
                CallerRoles = roleList,
                StepsUsed = steps,
                Phase = "agent-hitl",
                Note = rag.Note
            };
        }
        catch (OperationCanceledException)
        {
            return Stopped(question, roleList, steps, "Agent timed out.");
        }
    }

    public TicketRecord? Approve(Guid id, string approverUsername, IEnumerable<string> approverRoles, TicketStore tickets)
    {
        var roles = approverRoles.ToArray();
        if (!roles.Any(r => r is "Supervisor" or "Admin"))
        {
            return null;
        }

        var item = pending.Get(id);
        if (item is null || item.Status != "pending")
        {
            return null;
        }

        var draft = new TicketDraft
        {
            Title = item.Title,
            Body = item.Body,
            Severity = item.Severity
        };

        var ticket = tickets.Create(draft, item.RequestedBy, approverUsername, item.Id);
        item.Status = "approved";
        item.ResolvedUtc = DateTimeOffset.UtcNow;
        item.ResolvedBy = approverUsername;
        item.TicketId = ticket.Id;

        aiLog.Log(new AiCallRecord
        {
            Operation = "agent.tool.approve",
            Provider = CreateTicketTool.Name,
            User = approverUsername,
            InputPreview = id.ToString(),
            OutputPreview = $"ticket:{ticket.Id}",
            Meta = new Dictionary<string, string> { ["severity"] = ticket.Severity }
        });

        return ticket;
    }

    public bool Reject(Guid id, string rejectorUsername, IEnumerable<string> rejectorRoles)
    {
        var roles = rejectorRoles.ToArray();
        if (!roles.Any(r => r is "Supervisor" or "Admin"))
        {
            return false;
        }

        var item = pending.Get(id);
        if (item is null || item.Status != "pending")
        {
            return false;
        }

        item.Status = "rejected";
        item.ResolvedUtc = DateTimeOffset.UtcNow;
        item.ResolvedBy = rejectorUsername;

        aiLog.Log(new AiCallRecord
        {
            Operation = "agent.tool.reject",
            Provider = CreateTicketTool.Name,
            User = rejectorUsername,
            InputPreview = id.ToString(),
            OutputPreview = "rejected"
        });

        return true;
    }

    private static PendingApprovalDto ToDto(PendingApproval item) => new()
    {
        Id = item.Id,
        Tool = item.Tool,
        RequiresApproval = item.RequiresApproval,
        Title = item.Title,
        Body = item.Body,
        Severity = item.Severity,
        RequestedBy = item.RequestedBy,
        CreatedUtc = item.CreatedUtc,
        Status = item.Status
    };

    private static AgentAskResponse Stopped(string question, string[] roles, int steps, string note) =>
        new()
        {
            Status = "answered",
            Answer = "I stopped before completing the request (step/timeout limit).",
            Citations = [],
            Grounded = false,
            Question = question,
            CallerRoles = roles,
            StepsUsed = steps,
                Phase = "agent-hitl",
            Note = note
        };
}
