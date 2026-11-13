namespace AgentGateway.Core.Options;

public sealed class NewsOptions
{
    public const string SectionName = "News";
    public bool Enabled { get; set; }
    public string WordPressBaseUrl { get; set; } = "https://vahac.com";
    public string WordPressUser { get; set; } = "";
    public string WordPressAppPassword { get; set; } = "";
    public int MinItems { get; set; } = 35;
    public int MaxItems { get; set; } = 80;
    public int MinPerCategory { get; set; } = 1;
    public double MaxAiShare { get; set; } = 0.30;
    public int CallIntervalMilliseconds { get; set; } = 500;
    public int MaxAttempts { get; set; } = 6;
    public int RetryBaseMilliseconds { get; set; } = 2000;
    public int ItemTimeoutSeconds { get; set; } = 60;
    public bool NotifyTelegramOnPublish { get; set; } = true;
}
