namespace TelegramGateway.TestInfrastructure.Http;

public sealed record TelegramRequestCapture(string ChatId, string Text, string ParseMode, bool DisableNotification);
