using TelegramGateway.Api.Contracts;
using TelegramGateway.Core.Entities;

namespace TelegramGateway.Api.Mapping;

public sealed class DeliveryMapper : IDeliveryMapper
{
    public DeliveryResponse ToResponse(DeliveryEntity delivery)
    {
        var parts = delivery.Parts.OrderBy(part => part.Position).ToList();
        return new(delivery.Id, delivery.DigestDate, delivery.IdempotencyKey, delivery.Status,
            parts.Count, parts.Count(part => part.TelegramMessageId.HasValue),
            parts.Where(part => part.TelegramMessageId.HasValue).Select(part => part.TelegramMessageId!.Value).ToList(),
            delivery.RequiresReview, delivery.ErrorCode, delivery.CreatedUtc, delivery.UpdatedUtc, delivery.RetryNotBeforeUtc,
            parts.SelectMany(part => part.Attempts.OrderBy(attempt => attempt.CreatedUtc)
                .Select(attempt => new AttemptResponse(part.Position + 1, attempt.Status, attempt.CreatedUtc,
                    attempt.CompletedUtc, attempt.ErrorCode, attempt.TelegramMessageId))).ToList());
    }
}
