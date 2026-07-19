using Contoso.PolicyAssistant.Api.Features.Agent;
using Contoso.PolicyAssistant.Api.Features.Ask;
using Contoso.PolicyAssistant.Api.Features.Rag;
using Microsoft.Extensions.Options;
using Xunit;

namespace Contoso.PolicyAssistant.Api.Tests;

public class AgentHitlTests
{
    [Fact]
    public void Detector_finds_escalate_intent()
    {
        Assert.True(EscalateIntentDetector.IsEscalateIntent("Please escalate this safety incident"));
        Assert.False(EscalateIntentDetector.IsEscalateIntent("How many leave days do I get?"));
    }

    [Fact]
    public async Task Employee_cannot_propose_ticket()
    {
        var agent = BuildAgent();
        var result = await agent.HandleAsync(
            new AskQuestionRequest { Question = "Please create a ticket to escalate this safety incident" },
            "alice",
            ["Employee"]);

        Assert.Equal("forbiddenTool", result.Status);
        Assert.Null(result.PendingApproval);
    }

    [Fact]
    public async Task Supervisor_gets_pending_approval_not_ticket()
    {
        var tickets = new TicketStore();
        var pending = new PendingApprovalStore();
        var agent = BuildAgent(pending);

        var result = await agent.HandleAsync(
            new AskQuestionRequest { Question = "Escalate this Priority-1 safety incident at Dock 4" },
            "bob",
            ["Supervisor", "Employee"]);

        Assert.Equal("pendingApproval", result.Status);
        Assert.NotNull(result.PendingApproval);
        Assert.True(result.PendingApproval!.RequiresApproval);
        Assert.Equal(CreateTicketTool.Name, result.PendingApproval.Tool);
        Assert.Empty(tickets.List());
        Assert.True(result.StepsUsed <= 4);
    }

    [Fact]
    public async Task Approve_creates_ticket_once()
    {
        var tickets = new TicketStore();
        var pending = new PendingApprovalStore();
        var agent = BuildAgent(pending);

        var result = await agent.HandleAsync(
            new AskQuestionRequest { Question = "Please escalate and create a ticket for the safety incident" },
            "bob",
            ["Supervisor"]);

        Assert.NotNull(result.PendingApproval);
        var id = result.PendingApproval!.Id;

        var ticket = agent.Approve(id, "bob", ["Supervisor"], tickets);
        Assert.NotNull(ticket);
        Assert.Single(tickets.List());

        var again = agent.Approve(id, "bob", ["Supervisor"], tickets);
        Assert.Null(again);
        Assert.Single(tickets.List());
    }

    [Fact]
    public async Task Reject_discards_without_ticket()
    {
        var tickets = new TicketStore();
        var pending = new PendingApprovalStore();
        var agent = BuildAgent(pending);

        var result = await agent.HandleAsync(
            new AskQuestionRequest { Question = "Escalate safety incident — create a ticket" },
            "bob",
            ["Supervisor"]);

        Assert.True(agent.Reject(result.PendingApproval!.Id, "admin", ["Admin"]));
        Assert.Empty(tickets.List());
    }

    private static AgentAskHandler BuildAgent(PendingApprovalStore? pending = null)
    {
        pending ??= new PendingApprovalStore();
        var embeddings = new LexicalEmbeddingClient();
        var chat = new LexicalGroundedChatClient();
        var store = new InMemoryVectorStore();
        var text = "Escalate by creating a Priority-1 ticket. Notify the Safety Officer within 1 hour.";
        store.ReplaceAll(
        [
            new PolicyChunk
            {
                Id = "safety:0",
                DocumentId = "safety",
                Title = "Workplace Safety Escalation",
                FileName = "safety-escalate.md",
                AllowedRoles = ["Supervisor", "Admin"],
                Text = text,
                Embedding = LexicalEmbeddingClient.Embed(text)
            }
        ], "Lexical");

        var ask = new AskQuestionHandler(
            store,
            embeddings,
            chat,
            new NullAiCallLogger(),
            Options.Create(new RagOptions { TopK = 3, MinScore = 0.05f }));

        return new AgentAskHandler(
            ask,
            pending,
            new NullAiCallLogger(),
            Options.Create(new AgentOptions { MaxSteps = 4, TimeoutSeconds = 30 }));
    }
}
