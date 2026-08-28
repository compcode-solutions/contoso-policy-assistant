using Microsoft.Extensions.Hosting;

namespace Contoso.PolicyAssistant.Api.Features.Evals;

/// <summary>Seeds the last-run cache so the public panel is not empty on first visit.</summary>
public sealed class GoldenEvalWarmup(GoldenEvalService evals, ILogger<GoldenEvalWarmup> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() =>
        {
            try
            {
                evals.Warmup();
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Golden eval warmup failed — panel will run on first button click.");
            }
        }, cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
