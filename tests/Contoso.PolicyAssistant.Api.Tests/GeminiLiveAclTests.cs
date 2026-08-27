using Contoso.PolicyAssistant.Api.Features.Ask;
using Contoso.PolicyAssistant.Api.Features.Policies;
using Contoso.PolicyAssistant.Api.Features.Rag;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Contoso.PolicyAssistant.Api.Tests;

/// <summary>
/// Skips the live Gemini ACL test when no API key is present. Never prints the key.
/// </summary>
public sealed class GeminiLiveFactAttribute : FactAttribute
{
    public GeminiLiveFactAttribute()
    {
        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? Environment.GetEnvironmentVariable("Ai__Gemini__ApiKey");
        if (string.IsNullOrWhiteSpace(key))
        {
            Skip = "GEMINI_API_KEY not set — live Gemini ACL test skipped";
        }
    }
}

/// <summary>
/// Live Gemini ACL check against gemini-embedding-001 (768-d) + gemini-2.5-flash-lite.
/// Requires GEMINI_API_KEY. CI runs without it and skips this test.
/// </summary>
public class GeminiLiveAclTests
{
    [GeminiLiveFact]
    public async Task Employee_cannot_reach_supervisor_chunk_with_gemini()
    {
        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? Environment.GetEnvironmentVariable("Ai__Gemini__ApiKey")
            ?? throw new InvalidOperationException("Key disappeared after skip check.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        IEmbeddingClient embeddings = new GeminiEmbeddingClient(
            http,
            apiKey,
            GeminiEmbeddingClient.DefaultModel,
            GeminiEmbeddingClient.DefaultDimensions);
        IGroundedChatClient chat = new GeminiGroundedChatClient(
            http,
            apiKey,
            GeminiGroundedChatClient.DefaultModel);

        var catalog = new PolicyCatalog(
        [
            new PolicyDocument
            {
                Id = "leave",
                Title = "Leave Policy",
                FileName = "leave-policy.md",
                AllowedRoles = ["Employee", "Supervisor", "Admin"],
                BodyMarkdown = "Full-time employees receive 20 days of paid annual leave per calendar year."
            },
            new PolicyDocument
            {
                Id = "safety",
                Title = "Workplace Safety Escalation",
                FileName = "safety-escalate.md",
                AllowedRoles = ["Supervisor", "Admin"],
                BodyMarkdown = "Escalate by creating a Priority-1 ticket in the incident system within 1 hour for safety incidents. Notify the Safety Officer."
            }
        ]);

        var store = new InMemoryVectorStore();
        var ingest = new IngestService(catalog, store, embeddings, NullLogger<IngestService>.Instance);
        await ingest.IngestAsync();

        var handler = new AskQuestionHandler(
            store,
            embeddings,
            chat,
            new NullAiCallLogger(),
            Options.Create(new RagOptions { TopK = 4, MinScore = 0.12f }),
            new DemoQuota(int.MaxValue),
            new LexicalEmbeddingClient(),
            new LexicalGroundedChatClient(),
            hostedConfigured: true);

        var employee = await handler.HandleAsync(
            new AskQuestionRequest
            {
                Question = "What is the workplace safety escalation SOP Priority-1 ticket process?"
            },
            ["Employee"]);

        Assert.DoesNotContain("Priority-1", employee.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            employee.Citations,
            c => c.Title.Contains("safety", StringComparison.OrdinalIgnoreCase));
        Assert.True(employee.ChunksFilteredByRole >= 1);

        var supervisor = await handler.HandleAsync(
            new AskQuestionRequest
            {
                Question = "What is the workplace safety escalation SOP Priority-1 ticket process?"
            },
            ["Supervisor"]);

        Assert.True(supervisor.Grounded);
        Assert.Contains("Priority-1", supervisor.Answer, StringComparison.OrdinalIgnoreCase);
    }
}
