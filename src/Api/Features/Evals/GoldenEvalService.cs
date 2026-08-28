using System.Collections.Concurrent;
using System.Text.Json;
using Contoso.PolicyAssistant.Api.Features.Ask;
using Contoso.PolicyAssistant.Api.Features.Logging;
using Contoso.PolicyAssistant.Api.Features.Policies;
using Contoso.PolicyAssistant.Api.Features.Rag;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Contoso.PolicyAssistant.Api.Features.Evals;

/// <summary>
/// Runs the same 14 golden cases as tests/GoldenEvalTests — lexical RAG, ACL
/// before scoring. Does not touch the live Gemini index or the test file.
/// </summary>
public sealed class GoldenEvalService
{
    public static readonly HashSet<string> LeakCaseIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "employee-no-safety-leak",
        "employee-no-overtime-approval-leak"
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHostEnvironment _env;
    private readonly PolicyCatalog _catalog;
    private readonly object _runGate = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRunByIp = new();
    private GoldenEvalRunResult? _lastRun;

    public GoldenEvalService(IHostEnvironment env, PolicyCatalog catalog)
    {
        _env = env;
        _catalog = catalog;
    }

    public IReadOnlyList<GoldenCaseView> ListCases()
    {
        return LoadCases().Select(ToView).ToList();
    }

    public GoldenEvalSnapshot Snapshot()
    {
        if (_lastRun is null)
        {
            try { Warmup(); }
            catch { /* panel still lists cases; run button remains */ }
        }

        return new GoldenEvalSnapshot
        {
            Cases = ListCases(),
            LastRun = _lastRun
        };
    }

    public GoldenEvalRunResult Run(string ip, bool forceWithinLimit = false)
    {
        var now = DateTimeOffset.UtcNow;
        var key = string.IsNullOrWhiteSpace(ip) ? "unknown" : ip.Trim();

        if (!forceWithinLimit
            && _lastRunByIp.TryGetValue(key, out var prev)
            && now - prev < TimeSpan.FromHours(1)
            && _lastRun is not null)
        {
            return _lastRun with
            {
                Cached = true,
                RateLimited = true,
                RateLimitNote = "1 full run per IP per hour. Showing the last cached result."
            };
        }

        lock (_runGate)
        {
            if (!forceWithinLimit
                && _lastRunByIp.TryGetValue(key, out prev)
                && now - prev < TimeSpan.FromHours(1)
                && _lastRun is not null)
            {
                return _lastRun with
                {
                    Cached = true,
                    RateLimited = true,
                    RateLimitNote = "1 full run per IP per hour. Showing the last cached result."
                };
            }

            var result = ExecuteOnce();
            _lastRun = result;
            _lastRunByIp[key] = DateTimeOffset.UtcNow;
            return result;
        }
    }

    public GoldenEvalRunResult Warmup()
    {
        lock (_runGate)
        {
            if (_lastRun is not null) return _lastRun with { Cached = true };
            var result = ExecuteOnce();
            _lastRun = result;
            return result;
        }
    }

    private GoldenEvalRunResult ExecuteOnce()
    {
        var started = DateTimeOffset.UtcNow;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var cases = LoadCases();

        var embeddings = new LexicalEmbeddingClient();
        var chat = new LexicalGroundedChatClient();
        var store = new InMemoryVectorStore();
        var ingest = new IngestService(
            _catalog,
            store,
            embeddings,
            NullLogger<IngestService>.Instance);
        ingest.IngestAsync().GetAwaiter().GetResult();

        var handler = new AskQuestionHandler(
            store,
            embeddings,
            chat,
            NoOpAiCallLogger.Instance,
            Options.Create(new RagOptions
            {
                TopK = 4,
                MinScore = 0.12f
            }));

        var caseResults = new List<GoldenCaseRun>();
        foreach (var g in cases)
        {
            var result = handler.HandleAsync(
                new AskQuestionRequest { Question = g.Question },
                g.Roles).GetAwaiter().GetResult();

            var failures = Evaluate(g, result);
            caseResults.Add(new GoldenCaseRun
            {
                Id = g.Id,
                Passed = failures.Count == 0,
                Failures = failures,
                Grounded = result.Grounded,
                LeakCase = LeakCaseIds.Contains(g.Id)
            });
        }

        var passed = caseResults.Count(c => c.Passed);
        return new GoldenEvalRunResult
        {
            RanUtc = started,
            LatencyMs = (int)sw.ElapsedMilliseconds,
            Passed = passed,
            Total = caseResults.Count,
            Model = LexicalGroundedChatClient.Model,
            Provider = "Lexical",
            Cached = false,
            RateLimited = false,
            Cases = caseResults
        };
    }

    private static List<string> Evaluate(GoldenCaseFile g, AskQuestionResponse result)
    {
        // Same checks as GoldenEvalTests.Golden_evals_pass_lexical_rag — do not
        // drift. If you change one, change the other.
        var failures = new List<string>();

        if (result.Grounded != g.ExpectGrounded)
        {
            failures.Add($"grounded={result.Grounded} expected={g.ExpectGrounded}");
        }

        if (g.ExpectGrounded)
        {
            if (g.ExpectAnswerContainsAny is { Length: > 0 } &&
                !g.ExpectAnswerContainsAny.Any(s =>
                    result.Answer.Contains(s, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"answer missing any of [{string.Join(',', g.ExpectAnswerContainsAny)}]");
            }

            if (!string.IsNullOrWhiteSpace(g.ExpectCitationFileContains) &&
                !result.Citations.Any(c =>
                    c.Title.Contains(g.ExpectCitationFileContains, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"missing citation containing '{g.ExpectCitationFileContains}'");
            }
        }

        if (g.ForbidAnswerContainsAny is { Length: > 0 })
        {
            foreach (var bad in g.ForbidAnswerContainsAny)
            {
                if (result.Answer.Contains(bad, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"answer leaked '{bad}'");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(g.ForbidCitationFileContains) &&
            result.Citations.Any(c =>
                c.Title.Contains(g.ForbidCitationFileContains, StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add($"citation leaked file '{g.ForbidCitationFileContains}'");
        }

        return failures;
    }

    private List<GoldenCaseFile> LoadCases()
    {
        var path = FindGoldenPath();
        var cases = JsonSerializer.Deserialize<List<GoldenCaseFile>>(
            File.ReadAllText(path), JsonOpts) ?? [];
        if (cases.Count == 0)
        {
            throw new InvalidOperationException("evals/golden.json contained no cases.");
        }

        return cases;
    }

    private string FindGoldenPath()
    {
        var candidates = new List<string>
        {
            "/app/evals/golden.json",
            Path.Combine(_env.ContentRootPath, "evals", "golden.json")
        };

        var dir = new DirectoryInfo(_env.ContentRootPath);
        while (dir is not null)
        {
            candidates.Add(Path.Combine(dir.FullName, "evals", "golden.json"));
            dir = dir.Parent;
        }

        dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            candidates.Add(Path.Combine(dir.FullName, "evals", "golden.json"));
            dir = dir.Parent;
        }

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        throw new FileNotFoundException("evals/golden.json not found.");
    }

    private static GoldenCaseView ToView(GoldenCaseFile g)
    {
        return new GoldenCaseView
        {
            Id = g.Id,
            Question = g.Question,
            Roles = g.Roles,
            LeakCase = LeakCaseIds.Contains(g.Id),
            Asserts = Describe(g)
        };
    }

    private static string Describe(GoldenCaseFile g)
    {
        var bits = new List<string>
        {
            $"Asked as {string.Join(" + ", g.Roles)}",
            g.ExpectGrounded
                ? "must return a grounded answer"
                : "must refuse (not grounded)"
        };
        if (g.ExpectAnswerContainsAny is { Length: > 0 })
        {
            bits.Add("answer contains one of: " + string.Join(", ", g.ExpectAnswerContainsAny.Select(s => $"“{s}”")));
        }

        if (!string.IsNullOrWhiteSpace(g.ExpectCitationFileContains))
        {
            bits.Add($"citation title contains “{g.ExpectCitationFileContains}”");
        }

        if (g.ForbidAnswerContainsAny is { Length: > 0 })
        {
            bits.Add("answer must not contain: " + string.Join(", ", g.ForbidAnswerContainsAny.Select(s => $"“{s}”")));
        }

        if (!string.IsNullOrWhiteSpace(g.ForbidCitationFileContains))
        {
            bits.Add($"must not cite a document matching “{g.ForbidCitationFileContains}”");
        }

        if (LeakCaseIds.Contains(g.Id))
        {
            bits.Insert(0, "Leak case — the one that matters: an employee must not see supervisor-only content");
        }

        return string.Join(". ", bits) + ".";
    }
}

internal sealed class NoOpAiCallLogger : IAiCallLogger
{
    public static readonly NoOpAiCallLogger Instance = new();
    public void Log(AiCallRecord record) { }
}

public sealed class GoldenCaseFile
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

public sealed class GoldenCaseView
{
    public required string Id { get; init; }
    public required string Question { get; init; }
    public required string[] Roles { get; init; }
    public required bool LeakCase { get; init; }
    public required string Asserts { get; init; }
}

public sealed class GoldenCaseRun
{
    public required string Id { get; init; }
    public required bool Passed { get; init; }
    public required IReadOnlyList<string> Failures { get; init; }
    public required bool Grounded { get; init; }
    public required bool LeakCase { get; init; }
}

public sealed record GoldenEvalRunResult
{
    public required DateTimeOffset RanUtc { get; init; }
    public required int LatencyMs { get; init; }
    public required int Passed { get; init; }
    public required int Total { get; init; }
    public required string Model { get; init; }
    public required string Provider { get; init; }
    public required bool Cached { get; init; }
    public required bool RateLimited { get; init; }
    public string? RateLimitNote { get; init; }
    public required IReadOnlyList<GoldenCaseRun> Cases { get; init; }
}

public sealed class GoldenEvalSnapshot
{
    public required IReadOnlyList<GoldenCaseView> Cases { get; init; }
    public GoldenEvalRunResult? LastRun { get; init; }
}
