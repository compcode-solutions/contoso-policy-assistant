using Contoso.PolicyAssistant.Api.Features.Ask;
using Contoso.PolicyAssistant.Api.Features.Rag;
using Microsoft.Extensions.Configuration;
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

        // Free-form question (not in demo-script catalog) so hosted path is exercised.
        var result = await handler.HandleAsync(
            new AskQuestionRequest { Question = "What is the daily meal limit while traveling?" },
            ["Employee"]);

        Assert.True(result.Fallback);
        Assert.Equal("local-retrieval", result.FallbackReason);
        Assert.Contains("60", result.Answer);
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
            new AskQuestionRequest { Question = "What is the daily meal limit while traveling?" },
            ["Employee"]);

        Assert.True(result.Fallback);
        Assert.Equal("daily-ceiling", result.FallbackReason);
        Assert.Contains("quota reached", result.Note, StringComparison.OrdinalIgnoreCase);
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
                Question = "What is the workplace safety escalation Priority-1 ticket process?"
            },
            ["Employee"]);

        Assert.True(result.Fallback);
        Assert.Equal(DemoScriptCatalog.FallbackReason, result.FallbackReason);
        Assert.Equal(DemoScriptCatalog.ModelName, result.Model);
        Assert.False(result.Grounded);
        Assert.DoesNotContain("Priority-1", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            result.Citations,
            c => c.Title.Contains("safety", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.ChunksFilteredByRole >= 1);
    }

    [Fact]
    public async Task Demo_script_safety_differs_by_role_without_hosted_provider()
    {
        var handler = BuildHandler(
            hostedEmbeddings: new ThrowingEmbeddingClient(),
            quota: new DemoQuota(10),
            hostedConfigured: true);

        var question = "What should a supervisor do after a safety incident?";
        var employee = await handler.HandleAsync(
            new AskQuestionRequest { Question = question },
            ["Employee"]);
        var supervisor = await handler.HandleAsync(
            new AskQuestionRequest { Question = question },
            ["Supervisor", "Employee"]);

        Assert.Equal(DemoScriptCatalog.FallbackReason, employee.FallbackReason);
        Assert.Equal(DemoScriptCatalog.FallbackReason, supervisor.FallbackReason);
        Assert.False(employee.Grounded);
        Assert.DoesNotContain("Priority-1", employee.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.True(supervisor.Grounded);
        Assert.Contains("Priority-1", supervisor.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Safety Officer", supervisor.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.True(employee.ChunksFilteredByRole >= supervisor.ChunksFilteredByRole);
    }

    [Fact]
    public void DemoQuota_refund_restores_capacity_after_failed_hosted_call()
    {
        var quota = new DemoQuota(1);
        Assert.True(quota.TryConsumeHosted());
        Assert.False(quota.TryConsumeHosted());
        quota.RefundHosted();
        Assert.True(quota.TryConsumeHosted());
    }

    [Fact]
    public void Factory_gemini_without_key_stays_lexical()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Provider"] = "Gemini"
            })
            .Build();

        var set = AiClientFactory.Create(config);

        Assert.Equal("Gemini", set.RequestedProvider);
        Assert.Equal("Lexical", set.ActiveProvider);
        Assert.False(set.HostedConfigured);
        Assert.Equal("gemini-embedding-001", GeminiEmbeddingClient.DefaultModel);
        Assert.Equal(768, GeminiEmbeddingClient.DefaultDimensions);
        Assert.Equal("gemini-2.5-flash-lite", GeminiGroundedChatClient.DefaultModel);
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
            "Escalate by creating a Priority-1 ticket in the incident system within 1 hour for safety incidents. Notify the Safety Officer.";
        var mealText =
            "While traveling, the daily meal limit is $60 USD unless a higher cap is pre-approved in writing.";

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
            },
            new PolicyChunk
            {
                Id = "expense:0",
                DocumentId = "expense",
                Title = "Expense Policy",
                FileName = "expense-policy.md",
                AllowedRoles = ["Employee", "Supervisor", "Admin"],
                Text = mealText,
                Embedding = LexicalEmbeddingClient.Embed(mealText),
                LexicalEmbedding = LexicalEmbeddingClient.Embed(mealText)
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
