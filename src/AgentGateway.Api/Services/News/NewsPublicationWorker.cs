namespace AgentGateway.Api.Services.News;

/// <summary>Queue/Worker: the news rows in SQLite are the outbox; one worker publishes one issue at a time.</summary>
public sealed class NewsPublicationWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<NewsPublicationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var worked = await scope.ServiceProvider.GetRequiredService<NewsPublicationProcessor>().ProcessOnceAsync(stoppingToken);
                if (!worked) await Task.Delay(TimeSpan.FromSeconds(2), time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                // Exception text can contain issue content or SQL; a stable type name is enough to diagnose.
                logger.LogError("News publication worker failed ({FailureType}); requeueing interrupted issues.", exception.GetType().FullName);
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<NewsPublicationReconciler>().ReconcileAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(30), time, stoppingToken);
            }
        }
    }
}
