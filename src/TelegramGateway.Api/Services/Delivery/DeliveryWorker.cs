namespace TelegramGateway.Api.Services.Delivery;

/// <summary>The durable SQLite outbox is the queue; one worker sends to the configured chat.</summary>
public sealed class DeliveryWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<DeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<DeliveryProcessor>().ProcessOnceAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(1), time, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                // Exception text can include SQL/message content. Emit a stable code instead.
                logger.LogError("Delivery worker failed ({FailureType}); reconciling persisted attempts before continuing.",
                    exception.GetType().FullName);
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<DeliveryReconciler>().ReconcileAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), time, stoppingToken);
            }
        }
    }
}
