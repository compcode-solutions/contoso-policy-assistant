using Contoso.PolicyAssistant.Api.Features.Logging;
using Microsoft.Extensions.Logging;

namespace Contoso.PolicyAssistant.Api.Features.Rag;

/// <summary>
/// Tries <paramref name="primary"/> once; on 429 / 5xx / timeout / provider error,
/// falls back to <paramref name="fallback"/> once. No retry loops.
/// </summary>
public sealed class FallbackGroundedChatClient : IGroundedChatClient
{
    private readonly IGroundedChatClient _primary;
    private readonly IGroundedChatClient _fallback;
    private readonly ILogger? _logger;
    private readonly IAiCallLogger? _aiLog;

    public FallbackGroundedChatClient(
        IGroundedChatClient primary,
        IGroundedChatClient fallback,
        ILogger? logger = null,
        IAiCallLogger? aiLog = null)
    {
        _primary = primary;
        _fallback = fallback;
        _logger = logger;
        _aiLog = aiLog;
        ProviderName = primary.ProviderName;
        ModelName = primary.ModelName;
    }

    public string ProviderName { get; }
    public string ModelName { get; }

    public async Task<GroundedChatResult> AnswerAsync(
        string question,
        IReadOnlyList<RetrievedChunk> context,
        CancellationToken ct = default)
    {
        try
        {
            return await _primary.AnswerAsync(question, context, ct);
        }
        catch (Exception ex) when (ShouldFallback(ex, ct))
        {
            var reason = ReasonCode(ex, ct);
            _logger?.LogWarning(
                ex,
                "provider={Provider} fallback_reason={Reason}",
                _fallback.ProviderName,
                reason);
            _aiLog?.Log(new AiCallRecord
            {
                Operation = "rag.provider-fallback",
                Provider = _fallback.ProviderName,
                InputPreview = question,
                OutputPreview = reason,
                Meta = new Dictionary<string, string>
                {
                    ["provider"] = _fallback.ProviderName,
                    ["fallback_reason"] = reason,
                    ["from"] = _primary.ProviderName
                }
            });

            return await _fallback.AnswerAsync(question, context, ct);
        }
    }

    public static bool ShouldFallback(Exception ex, CancellationToken ct)
    {
        if (ex is OperationCanceledException)
            return !ct.IsCancellationRequested;
        if (ex is TimeoutException) return true;
        if (ex is LlmHttpException llm)
        {
            var code = (int?)llm.StatusCode;
            // 401/403: a revoked or expired IFM key must not take the assistant down.
            return code is null or 401 or 403 or 429 or (>= 500 and <= 599);
        }
        if (ex is HttpRequestException http)
        {
            var code = (int?)http.StatusCode;
            return code is null or 429 or (>= 500 and <= 599);
        }
        return false;
    }

    public static string ReasonCode(Exception ex, CancellationToken ct)
    {
        if (ex is OperationCanceledException && !ct.IsCancellationRequested) return "timeout";
        if (ex is TimeoutException) return "timeout";
        if (ex is LlmHttpException llm) return llm.ReasonCode;
        if (ex is HttpRequestException http)
        {
            var code = (int?)http.StatusCode;
            if (code == 429) return "429";
            if (code is >= 500 and <= 599) return "5xx";
            return code?.ToString() ?? "http";
        }
        return "error";
    }
}
