namespace TelegramGateway.Core.Abstractions;

public sealed record TelegramSendResult(
    TelegramSendStatus Status, long? MessageId = null, string? ErrorCode = null, int? RetryAfterSeconds = null);
