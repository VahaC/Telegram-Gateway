using TelegramGateway.Core.Enums;

namespace TelegramGateway.Api.Contracts;

public sealed record AttemptResponse(
    int Part, AttemptStatus Status, DateTime CreatedUtc, DateTime? CompletedUtc,
    string? ErrorCode, long? TelegramMessageId);
