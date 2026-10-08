using TelegramGateway.Core.Enums;

namespace TelegramGateway.Api.Contracts;

public sealed record DeliveryResponse(
    Guid Id, DateOnly? DigestDate, string IdempotencyKey, DeliveryStatus Status,
    int MessageCount, int DeliveredMessageCount, IReadOnlyList<long> TelegramMessageIds,
    bool RequiresReview, string? ErrorCode, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? RetryNotBeforeUtc,
    IReadOnlyList<AttemptResponse> Attempts);
