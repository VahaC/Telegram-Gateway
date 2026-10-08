namespace TelegramGateway.Core.Abstractions;

public enum TelegramSendStatus
{
    Delivered = 0,
    Retryable = 1,
    PermanentFailure = 2,
    Ambiguous = 3
}
