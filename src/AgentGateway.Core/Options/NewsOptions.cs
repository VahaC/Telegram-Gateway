namespace AgentGateway.Core.Options;

public sealed class NewsOptions
{
    public const string SectionName = "News";
    public bool Enabled { get; set; }
    public string WordPressBaseUrl { get; set; } = "https://vahac.com";
    public string WordPressUser { get; set; } = "";
    public string WordPressAppPassword { get; set; } = "";
    // 2026-11-15: the 35-item floor and a mandatory item per category made the routine give up on thin news weeks;
    // the routine now decides the size within 5..40 itself.
    public int MinItems { get; set; } = 5;
    public int MaxItems { get; set; } = 40;
    public int MinPerCategory { get; set; } = 0;
    public double MaxAiShare { get; set; } = 0.30;
    public int CallIntervalMilliseconds { get; set; } = 500;
    public int MaxAttempts { get; set; } = 6;
    public int RetryBaseMilliseconds { get; set; } = 2000;
    public int ItemTimeoutSeconds { get; set; } = 60;
    public bool NotifyTelegramOnPublish { get; set; } = true;
    /// <summary>Collect the catalog's RSS/Atom feeds for get_news_feed (ADR 0004). Needs <see cref="Enabled"/> as well.</summary>
    public bool FeedsEnabled { get; set; } = true;
    /// <summary>The feed catalog; a relative path is resolved against the application folder, where the build ships the file.</summary>
    public string FeedCatalogPath { get; set; } = "NewsFeeds/news-feeds.json";
    public int FeedPollMinutes { get; set; } = 60;
    /// <summary>Names the gateway as what it is, a personal feed reader; a browser User-Agent would pretend to be a person.</summary>
    public string FeedUserAgent { get; set; } = "AgentGateway-NewsFeed/1.0 (personal feed reader)";
    public int FeedParallelism { get; set; } = 4;
    /// <summary>Daily check that an issue arrived; alerts once on Telegram when it did not. Needs <see cref="Enabled"/> as well.</summary>
    public bool WatchdogEnabled { get; set; } = true;
    /// <summary>Wall-clock HH:mm in <see cref="WatchdogTimeZone"/>.</summary>
    public string WatchdogTime { get; set; } = "06:00";
    /// <summary>IANA zone of <see cref="WatchdogTime"/>; UTC by default because the routine's cron runs in UTC.</summary>
    public string WatchdogTimeZone { get; set; } = "UTC";
}
