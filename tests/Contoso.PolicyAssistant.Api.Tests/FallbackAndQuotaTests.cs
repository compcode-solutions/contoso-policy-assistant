using Contoso.PolicyAssistant.Api.Features.Ask;
using Contoso.PolicyAssistant.Api.Features.Rag;
using Microsoft.Extensions.Options;
using Xunit;

namespace Contoso.PolicyAssistant.Api.Tests;

public class FallbackAndQuotaTests
{
    [Fact]
    public void DemoQuota_blocks_after_ceiling()
    {
        var quota = new DemoQuota(2);
        Assert.True(quota.TryConsumeHosted());
        Assert.True(quota.TryConsumeHosted());
        Assert.False(quota.TryConsumeHosted());
        Assert.Equal(0, quota.RemainingToday);
        Assert.True(quota.CapReached);
    }

    [Fact]
    public async Task Handler_falls_back_to_lexical_when_hosted_throws()
    {
        var handler = BuildHandler(
            hostedEmbeddings: new ThrowingEmbeddingClient(),
            quota: new DemoQuota(10),
            hostedConfigured: true);

        var result = await handler.HandleAsync(
            new AskQuestionRequest { Question = "How many leave days do I get each year?" },
            ["Employee"]);

        Assert.True(result.Fallback);
        Assert.Equal("provider-error", result.FallbackReason);
        Assert.Contains("20", result.Answer);
        Assert.Contains("lexical", result.Model, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Priority-1", result.Answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handler_falls_back_to_lexical_when_daily_cap_hit()
    {
        var quota = new DemoQuota(1);
        Assert.True(quota.TryConsumeHosted());

        var handler = BuildHandler(
            hostedEmbeddings: new ThrowingEmbeddingClient(),
            quota: quota,
            hostedConfigured: true);

        var result = await handler.HandleAsync(
            new AskQuestionRequest { Question = "How many leave days do I get each year?" },
            ["Employee"]);

        Assert.True(result.Fallback);
        Assert.Equal("daily-ceiling", result.FallbackReason);
        Assert.Contains("20", result.Answer);
        Assert.Contains("Demo quota reached", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fallback_still_blocks_employee_from_supervisor_chunk()
    {
        var handler = BuildHandler(
            hostedEmbeddings: new ThrowingEmbeddingClient(),
            quota: new DemoQuota(10),
            hostedConfigured: true);

        var result = await handler.HandleAsync(
            new AskQuestionRequest
            {
                Question = "What is the workplace safety escalation SOP Priority-1 ticket process?"
            },
            ["Employee"]);

        Assert.True(result.Fallback);
        Assert.DoesNotContain("Priority-1", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            result.Citations,
            c => c.Title.Contains("safety", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.ChunksFilteredByRole >= 1);
    }

    private static AskQuestionHandler BuildHandler(
        IEmbeddingClient hostedEmbeddings,
        DemoQuota quota,
        bool hostedConfigured)
    {
        var lexicalEmb = new LexicalEmbeddingClient();
        var lexicalChat = new LexicalGroundedChatClient();
        var store = new InMemoryVectorStore();

        var leaveText =
            "Full-time employees receive 20 days of paid annual leave per calendar year. Leave requests must be submitted at least 5 business days in advance.";
        var safetyText =
            "Escalate by creating a Priority-1 ticket in the incident system within 1 hour for safety incidents.";

        store.ReplaceAll(
        [
            new PolicyChunk
            {
                Id = "leave:0",
                DocumentId = "leave",
                Title = "Leave Policy",
                FileName = "leave-policy.md",
                AllowedRoles = ["Employee", "Supervisor", "Admin"],
                Text = leaveText,
                Embedding = LexicalEmbeddingClient.Embed(leaveText),
                LexicalEmbedding = LexicalEmbeddingClient.Embed(leaveText)
            },
            new PolicyChunk
            {
                Id = "safety:0",
                DocumentId = "safety",
                Title = "Workplace Safety Escalation",
                FileName = "safety-escalate.md",
                AllowedRoles = ["Supervisor", "Admin"],
                Text = safetyText,
                Embedding = LexicalEmbeddingClient.Embed(safetyText),
                LexicalEmbedding = LexicalEmbeddingClient.Embed(safetyText)
            }
        ], "OpenAI");

        return new AskQuestionHandler(
            store,
            hostedEmbeddings,
            new ThrowingChatClient(),
            new NullAiCallLogger(),
            Options.Create(new RagOptions { TopK = 4, MinScore = 0.12f }),
            quota,
            lexicalEmb,
            lexicalChat,
            hostedConfigured);
    }

    private sealed class ThrowingEmbeddingClient : IEmbeddingClient
    {
        public string ProviderName => "OpenAI";
        public string ModelName => "text-embedding-3-small";

        public Task<EmbeddingCallResult> EmbedAsync(string text, CancellationToken ct = default) =>
            throw new HttpRequestException("simulated provider error");

        public Task<EmbeddingBatchResult> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            throw new HttpRequestException("simulated provider error");
    }

    private sealed class ThrowingChatClient : IGroundedChatClient
    {
        public string ProviderName => "OpenAI";
        public string ModelName => "gpt-4o-mini";

        public Task<GroundedChatResult> AnswerAsync(
            string question,
            IReadOnlyList<RetrievedChunk> context,
            CancellationToken ct = default) =>
            throw new HttpRequestException("simulated provider error");
    }
}
