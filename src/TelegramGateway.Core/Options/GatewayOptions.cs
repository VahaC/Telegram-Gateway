namespace TelegramGateway.Core.Options;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";
    public string BotToken { get; set; } = "";
    public string ChatId { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string DataPath { get; set; } = "Data";
    public int MessageLength { get; set; } = 4096;
    public int MaxParts { get; set; } = 64;
    public int MaxAttempts { get; set; } = 4;
    public int RetryBaseMilliseconds { get; set; } = 1000;
    public int MaxRetryAfterSeconds { get; set; } = 300;
    public int MessageIntervalMilliseconds { get; set; } = 1100;
    public int RequestLimitPerMinute { get; set; } = 30;
    public string[] KnownProxies { get; set; } = [];
    public string[] AllowedHosts { get; set; } = ["localhost"];
    public bool EnableMcp { get; set; }
}
