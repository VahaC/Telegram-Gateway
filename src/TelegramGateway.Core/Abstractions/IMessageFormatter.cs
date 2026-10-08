namespace TelegramGateway.Core.Abstractions;

public interface IMessageFormatter
{
    FormattingResult Format(string text, string format, int messageLength, int maxParts);
}
