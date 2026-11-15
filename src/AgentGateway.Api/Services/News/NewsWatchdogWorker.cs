using Microsoft.Extensions.Options;
using AgentGateway.Core.Options;

namespace AgentGateway.Api.Services.News;

/// <summary>Periodic loop of the news watchdog; all logic sits in <see cref="NewsWatchdog.CheckOnceAsync"/>. Active only with News:Enabled and News:WatchdogEnabled.</summary>
public sealed class NewsWatchdogWorker(IServiceScopeFactory scopes, IOptions<NewsOptions> options, TimeProvider time,
    ILogger<NewsWatchdogWorker> logger) : BackgroundService
{
    /// <summary>The alert lands within a minute of the check time; a check is two indexed reads, so the cost is nil.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled || !options.Value.WatchdogEnabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<NewsWatchdog>().CheckOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                // Exception text can contain SQL; a stable type name is enough to diagnose.
                logger.LogError("News watchdog check failed ({FailureType}).", exception.GetType().FullName);
            }
            try { await Task.Delay(TickInterval, time, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
