using Microsoft.Extensions.Options;
using AgentGateway.Core.Options;

namespace AgentGateway.Api.Services.News;

/// <summary>
/// Periodic loop of the feed collector (ADR 0004): applies the catalog once, polls shortly after start, then every
/// News:FeedPollMinutes. Active only with News:Enabled and News:FeedsEnabled.
/// </summary>
public sealed class NewsFeedWorker(IServiceScopeFactory scopes, IOptions<NewsOptions> options, TimeProvider time,
    ILogger<NewsFeedWorker> logger) : BackgroundService
{
    /// <summary>Lets startup, the publication worker and the first MCP calls settle before a few hundred requests go out.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.FeedsEnabled) return;
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<NewsFeedCatalogSync>().SyncAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        catch (Exception exception)
        {
            // The polls below still run against whatever the table holds.
            logger.LogError("News feed catalog sync failed ({FailureType}).", exception.GetType().FullName);
        }
        try { await Task.Delay(StartupDelay, time, stoppingToken); }
        catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<NewsFeedPoller>().PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                // Exception text can contain entry content or SQL; a stable type name is enough to diagnose.
                logger.LogError("News feed poll failed ({FailureType}).", exception.GetType().FullName);
            }
            try { await Task.Delay(TimeSpan.FromMinutes(settings.FeedPollMinutes), time, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
