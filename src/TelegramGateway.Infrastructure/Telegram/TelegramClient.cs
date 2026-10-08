using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TelegramGateway.Core.Abstractions;
using TelegramGateway.Core.Options;

namespace TelegramGateway.Infrastructure.Telegram;

internal sealed class TelegramClient(IHttpClientFactory clients, IOptions<GatewayOptions> options) : ITelegramClient
{
    public const string HttpClientName = "Telegram";

    public async Task<TelegramSendResult> SendAsync(string html, bool disableNotification,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = options.Value;
            using var response = await clients.CreateClient(HttpClientName).PostAsJsonAsync(
                $"bot{settings.BotToken}/sendMessage",
                new TelegramSendRequest(settings.ChatId, html, "HTML", disableNotification), cancellationToken);
            if ((int)response.StatusCode >= 500)
                return new(TelegramSendStatus.Retryable, ErrorCode: "telegram_server_error");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = body.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new(TelegramSendStatus.Ambiguous, ErrorCode: "telegram_invalid_response");
            if (response.IsSuccessStatusCode && root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True
                && root.TryGetProperty("result", out var result) && result.TryGetProperty("message_id", out var messageId)
                && messageId.TryGetInt64(out var identifier) && identifier > 0)
                return new(TelegramSendStatus.Delivered, identifier);

            var errorCode = root.TryGetProperty("error_code", out var error) && error.TryGetInt32(out var code)
                ? code : (int)response.StatusCode;
            if (errorCode == 429)
            {
                var delay = root.TryGetProperty("parameters", out var parameters)
                    && parameters.TryGetProperty("retry_after", out var retry) && retry.TryGetInt32(out var seconds)
                    ? Math.Max(1, seconds) : 1;
                return new(TelegramSendStatus.Retryable, ErrorCode: "telegram_rate_limited", RetryAfterSeconds: delay);
            }
            if (errorCode >= 500)
                return new(TelegramSendStatus.Retryable, ErrorCode: "telegram_server_error");
            if (errorCode is >= 400 and < 500)
                return new(TelegramSendStatus.PermanentFailure, ErrorCode: $"telegram_{errorCode}");
            return new(TelegramSendStatus.Ambiguous, ErrorCode: "telegram_invalid_response");
        }
        catch (HttpRequestException exception) when (exception.HttpRequestError is HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError
            || exception.InnerException is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused
                or System.Net.Sockets.SocketError.HostUnreachable or System.Net.Sockets.SocketError.NetworkUnreachable })
        {
            return new(TelegramSendStatus.Retryable, ErrorCode: "telegram_connection_failed");
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            // ADR 0001: no response does not prove no side effect; never log the exception's token-bearing URL.
            return new(TelegramSendStatus.Ambiguous, ErrorCode: "telegram_outcome_unknown");
        }
    }
}
