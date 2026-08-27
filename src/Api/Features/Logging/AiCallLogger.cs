using System.Text.Json;
using System.Text.RegularExpressions;

namespace Contoso.PolicyAssistant.Api.Features.Logging;

public interface IAiCallLogger
{
    void Log(AiCallRecord record);
}

public sealed class AiCallRecord
{
    public DateTimeOffset Utc { get; init; } = DateTimeOffset.UtcNow;
    public required string Operation { get; init; }
    public required string Provider { get; init; }
    public string? User { get; init; }
    public string? InputPreview { get; init; }
    public string? OutputPreview { get; init; }
    public int? DurationMs { get; init; }
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
    public int? EmbeddingTokens { get; init; }
    public int? TotalTokens { get; init; }
    public Dictionary<string, string>? Meta { get; init; }
}

/// <summary>
/// Appends redacted AI call traces to logs/ai-yyyyMMdd.jsonl under the content root.
/// </summary>
public sealed class AiCallLogger(IHostEnvironment env, ILogger<AiCallLogger> logger) : IAiCallLogger
{
    private static readonly Regex SecretRx = new(
        @"(sk-[A-Za-z0-9_\-]{10,}|Bearer\s+[A-Za-z0-9\-_\.]{10,}|api[_-]?key\s*[:=]\s*\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly object _gate = new();

    public void Log(AiCallRecord record)
    {
        try
        {
            var dir = Path.Combine(env.ContentRootPath, "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"ai-{DateTime.UtcNow:yyyyMMdd}.jsonl");

            var safe = new
            {
                utc = record.Utc,
                operation = record.Operation,
                provider = record.Provider,
                user = record.User,
                inputPreview = Redact(Truncate(record.InputPreview, 800)),
                outputPreview = Redact(Truncate(record.OutputPreview, 800)),
                durationMs = record.DurationMs,
                promptTokens = record.PromptTokens,
                completionTokens = record.CompletionTokens,
                embeddingTokens = record.EmbeddingTokens,
                totalTokens = record.TotalTokens,
                meta = record.Meta
            };

            var line = JsonSerializer.Serialize(safe);
            lock (_gate)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write AI call log");
        }
    }

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s[..max] + "…";

    private static string? Redact(string? s) =>
        s is null ? null : SecretRx.Replace(s, "[REDACTED]");
}
