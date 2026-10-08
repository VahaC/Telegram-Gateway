using Microsoft.EntityFrameworkCore;
using TelegramGateway.Core.Enums;
using TelegramGateway.Infrastructure.Persistence;

namespace TelegramGateway.Api.Services.Delivery;

public sealed class DeliveryReconciler(ApplicationDbContext database, TimeProvider time)
{
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var interrupted = await database.Deliveries.Include(row => row.Parts).ThenInclude(part => part.Attempts)
            .Where(row => row.Status == DeliveryStatus.Sending).ToListAsync(cancellationToken);
        foreach (var delivery in interrupted)
        {
            foreach (var attempt in delivery.Parts.SelectMany(part => part.Attempts).Where(attempt => attempt.Status == AttemptStatus.Started))
            {
                attempt.Status = AttemptStatus.Ambiguous;
                attempt.ErrorCode = "process_interrupted";
                attempt.CompletedUtc = time.GetUtcNow().UtcDateTime;
            }
            delivery.RequiresReview = delivery.Parts.SelectMany(part => part.Attempts).Any(attempt => attempt.Status == AttemptStatus.Ambiguous);
            delivery.Status = delivery.Parts.All(part => part.TelegramMessageId.HasValue) ? DeliveryStatus.Delivered
                : delivery.RequiresReview ? (delivery.Parts.Any(part => part.TelegramMessageId.HasValue)
                    ? DeliveryStatus.PartiallyDelivered : DeliveryStatus.Failed) : DeliveryStatus.Pending;
            delivery.ErrorCode = delivery.RequiresReview ? "process_interrupted" : null;
            delivery.UpdatedUtc = time.GetUtcNow().UtcDateTime;
        }
        await database.SaveChangesAsync(cancellationToken);
    }
}
