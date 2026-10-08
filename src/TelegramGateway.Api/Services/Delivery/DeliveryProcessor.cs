using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TelegramGateway.Core.Abstractions;
using TelegramGateway.Core.Entities;
using TelegramGateway.Core.Enums;
using TelegramGateway.Core.Options;
using TelegramGateway.Infrastructure.Persistence;

namespace TelegramGateway.Api.Services.Delivery;

public sealed class DeliveryProcessor(ApplicationDbContext database, ITelegramClient telegram,
    IOptions<GatewayOptions> options, TimeProvider time, ILogger<DeliveryProcessor> logger)
{
    public async Task<bool> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        // Telegram flood control can cover the configured chat, not just this delivery or process.
        var notBefore = await database.Deliveries.AsNoTracking().OrderByDescending(row => row.RetryNotBeforeUtc)
            .Select(row => row.RetryNotBeforeUtc).FirstOrDefaultAsync(cancellationToken);
        if (notBefore > time.GetUtcNow().UtcDateTime) return false;
        var identifier = await database.Deliveries.Where(row => row.Status == DeliveryStatus.Pending)
            .OrderBy(row => row.CreatedUtc).Select(row => (Guid?)row.Id).FirstOrDefaultAsync(cancellationToken);
        if (identifier is null) return false;
        var claimed = await database.Deliveries.Where(row => row.Id == identifier && row.Status == DeliveryStatus.Pending)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, DeliveryStatus.Sending)
                .SetProperty(row => row.UpdatedUtc, time.GetUtcNow().UtcDateTime), cancellationToken);
        if (claimed == 0) return false;
        var delivery = await database.Deliveries.Include(row => row.Parts).ThenInclude(part => part.Attempts)
            .SingleAsync(row => row.Id == identifier, cancellationToken);
        foreach (var part in delivery.Parts.OrderBy(part => part.Position).Where(part => part.TelegramMessageId is null))
        {
            for (var index = 0; index < options.Value.MaxAttempts; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attempt = new DeliveryAttemptEntity { CreatedUtc = time.GetUtcNow().UtcDateTime, Status = AttemptStatus.Started };
                part.Attempts.Add(attempt);
                database.DeliveryAttempts.Add(attempt); // Preassigned GUIDs must explicitly enter Added state on tracked graphs.
                // Persist intent before the network write; an interrupted intent requires human review on boot.
                await database.SaveChangesAsync(cancellationToken);
                var result = await telegram.SendAsync(part.Html, delivery.DisableNotification, cancellationToken);
                attempt.CompletedUtc = time.GetUtcNow().UtcDateTime;
                attempt.ErrorCode = result.ErrorCode;
                attempt.TelegramMessageId = result.MessageId;
                attempt.Status = result.Status switch
                {
                    TelegramSendStatus.Delivered => AttemptStatus.Delivered,
                    TelegramSendStatus.Ambiguous => AttemptStatus.Ambiguous,
                    _ => AttemptStatus.Rejected
                };
                if (result.Status == TelegramSendStatus.Delivered) part.TelegramMessageId = result.MessageId;
                if (result.RetryAfterSeconds is { } retryAfter)
                    delivery.RetryNotBeforeUtc = time.GetUtcNow().UtcDateTime.AddSeconds(retryAfter);
                // Do not use the canceled host token here: recording the observed outcome is still essential.
                await database.SaveChangesAsync(CancellationToken.None);
                if (result.Status == TelegramSendStatus.Delivered)
                {
                    await PauseAsync(options.Value.MessageIntervalMilliseconds, cancellationToken);
                    break;
                }
                if (result.Status == TelegramSendStatus.Retryable && index + 1 < options.Value.MaxAttempts
                    && (result.RetryAfterSeconds ?? 0) <= options.Value.MaxRetryAfterSeconds)
                {
                    var delay = result.RetryAfterSeconds is { } seconds ? seconds * 1000
                        : options.Value.RetryBaseMilliseconds * (1 << index);
                    await PauseAsync(delay, cancellationToken);
                    continue;
                }
                delivery.RequiresReview = result.Status == TelegramSendStatus.Ambiguous;
                delivery.ErrorCode = result.ErrorCode;
                delivery.Status = delivery.Parts.Any(item => item.TelegramMessageId.HasValue)
                    ? DeliveryStatus.PartiallyDelivered : DeliveryStatus.Failed;
                delivery.UpdatedUtc = time.GetUtcNow().UtcDateTime;
                await database.SaveChangesAsync(CancellationToken.None);
                logger.LogWarning("Delivery {DeliveryId} stopped at part {Part} with {ErrorCode}.", delivery.Id, part.Position + 1, delivery.ErrorCode);
                return true;
            }
        }
        delivery.Status = DeliveryStatus.Delivered;
        delivery.ErrorCode = null;
        delivery.UpdatedUtc = time.GetUtcNow().UtcDateTime;
        await database.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Delivery {DeliveryId} completed with {MessageCount} parts.", delivery.Id, delivery.Parts.Count);
        return true;
    }

    private Task PauseAsync(int milliseconds, CancellationToken cancellationToken) => milliseconds == 0
        ? Task.CompletedTask : Task.Delay(TimeSpan.FromMilliseconds(milliseconds), time, cancellationToken);
}
