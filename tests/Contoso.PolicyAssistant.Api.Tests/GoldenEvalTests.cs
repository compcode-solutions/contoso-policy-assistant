using System.Text.Json;
using Contoso.PolicyAssistant.Api.Features.Ask;
using Contoso.PolicyAssistant.Api.Features.Policies;
using Contoso.PolicyAssistant.Api.Features.Rag;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Contoso.PolicyAssistant.Api.Tests;

public class GoldenEvalTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task Golden_evals_pass_lexical_rag()
    {
        var goldenPath = FindGoldenPath();
        var cases = JsonSerializer.Deserialize<List<GoldenCase>>(
            await File.ReadAllTextAsync(goldenPath), JsonOpts) ?? [];

        Assert.True(cases.Count >= 12, $"Expected at least 12 golden cases, found {cases.Count}");
        Assert.Contains(cases, g => g.Id == "employee-no-safety-leak");
        Assert.Contains(cases, g => g.Id == "supervisor-safety");

        var handler = await BuildHandlerFromPoliciesAsync();
        var failures = new List<string>();
        var passed = 0;

        foreach (var g in cases)
        {
            var result = await handler.HandleAsync(
                new AskQuestionRequest { Question = g.Question },
                g.Roles);

            if (result.Grounded != g.ExpectGrounded)
            {
                failures.Add($"{g.Id}: grounded={result.Grounded} expected={g.ExpectGrounded}; answer={result.Answer}");
                continue;
            }

            if (g.ExpectGrounded)
            {
                if (g.ExpectAnswerContainsAny is { Length: > 0 } &&
                    !g.ExpectAnswerContainsAny.Any(s =>
                        result.Answer.Contains(s, StringComparison.OrdinalIgnoreCase)))
                {
                    failures.Add($"{g.Id}: answer missing any of [{string.Join(',', g.ExpectAnswerContainsAny)}]");
                }

                if (!string.IsNullOrWhiteSpace(g.ExpectCitationFileContains) &&
                    !result.Citations.Any(c =>
                        c.Title.Contains(g.ExpectCitationFileContains, StringComparison.OrdinalIgnoreCase)))
                {
                    failures.Add($"{g.Id}: missing citation containing '{g.ExpectCitationFileContains}'");
                }
            }

            if (g.ForbidAnswerContainsAny is { Length: > 0 })
            {
                foreach (var bad in g.ForbidAnswerContainsAny)
                {
                    if (result.Answer.Contains(bad, StringComparison.OrdinalIgnoreCase))
                    {
                        failures.Add($"{g.Id}: answer leaked '{bad}'");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(g.ForbidCitationFileContains) &&
                result.Citations.Any(c =>
                    c.Title.Contains(g.ForbidCitationFileContains, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"{g.Id}: citation leaked file '{g.ForbidCitationFileContains}'");
            }

            if (failures.Count == 0 || failures.TrueForAll(f => !f.StartsWith(g.Id + ":", StringComparison.Ordinal)))
                passed++;
        }

        Assert.True(
            failures.Count == 0,
            $"Golden evals {passed}/{cases.Count} passed. Failures:\n" + string.Join('\n', failures));
    }

    private static async Task<AskQuestionHandler> BuildHandlerFromPoliciesAsync()
    {
        var policiesDir = FindPoliciesDir();
        var catalog = PolicyCatalog.LoadFromDirectory(policiesDir);
        var embeddings = new LexicalEmbeddingClient();
        var chat = new LexicalGroundedChatClient();
        var store = new InMemoryVectorStore();
        var ingest = new IngestService(
            catalog,
            store,
            embeddings,
            NullLogger<IngestService>.Instance);

        await ingest.IngestAsync();

        return new AskQuestionHandler(
            store,
            embeddings,
            chat,
            new NullAiCallLogger(),
            Options.Create(new RagOptions { TopK = 4, MinScore = 0.12f }));
    }

    private static string FindGoldenPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "evals", "golden.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("evals/golden.json not found from " + AppContext.BaseDirectory);
    }

    private static string FindPoliciesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "data", "policies");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("data/policies not found");
    }

    private sealed class GoldenCase
    {
        public string Id { get; set; } = "";
        public string Question { get; set; } = "";
        public string[] Roles { get; set; } = [];
        public bool ExpectGrounded { get; set; }
        public string[]? ExpectAnswerContainsAny { get; set; }
        public string? ExpectCitationFileContains { get; set; }
        public string[]? ForbidAnswerContainsAny { get; set; }
        public string? ForbidCitationFileContains { get; set; }
    }
}
