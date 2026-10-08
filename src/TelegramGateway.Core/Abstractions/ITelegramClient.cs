namespace TelegramGateway.Core.Abstractions;

/// <summary>Adapter for one send attempt; ambiguous writes are never replayed here.</summary>
public interface ITelegramClient
{
    Task<TelegramSendResult> SendAsync(string html, bool disableNotification, CancellationToken cancellationToken);
}
