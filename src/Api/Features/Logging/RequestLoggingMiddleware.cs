using System.Diagnostics;

namespace Contoso.PolicyAssistant.Api.Features.Logging;

/// <summary>
/// Lightweight request timing logs. Azure mapping: Application Insights request telemetry.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            sw.Stop();
            var path = context.Request.Path.Value ?? "/";
            // Skip noisy static/swagger asset spam in local runs
            if (!path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(
                    "HTTP {Method} {Path} => {StatusCode} in {ElapsedMs}ms",
                    context.Request.Method,
                    path,
                    context.Response.StatusCode,
                    sw.ElapsedMilliseconds);
            }
        }
    }
}
