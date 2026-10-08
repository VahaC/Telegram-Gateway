using System.Text.Json.Serialization;

namespace TelegramGateway.Infrastructure.Telegram;

internal sealed record TelegramSendRequest(
    [property: JsonPropertyName("chat_id")] string ChatId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("parse_mode")] string ParseMode,
    [property: JsonPropertyName("disable_notification")] bool DisableNotification);
