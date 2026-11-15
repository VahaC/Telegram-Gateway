namespace AgentGateway.Core.Entities;

/// <summary>One article a feed announced, stored as the feed carried it: the gateway never fetches the article page.</summary>
public class NewsFeedEntryEntity : AuditableEntity
{
    public Guid FeedId { get; set; }
    /// <summary>Unique across all feeds: the same article in two feeds is one entry, the first feed that carried it keeps it.</summary>
    public string CanonicalUrl { get; set; } = "";
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>The date from the publisher's own feed; null when the feed had no parseable date.</summary>
    public DateTime? PublishedUtc { get; set; }
    /// <summary>Plain text: HTML stripped, whitespace collapsed, at most 4000 characters.</summary>
    public string Summary { get; set; } = "";
    /// <summary>An https picture named by the feed itself (media:content, media:thumbnail, an image enclosure or the first img).</summary>
    public string? ImageUrl { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    /// <summary>
    /// PublishedUtc, or FirstSeenUtc when the feed gave no date. Stored and indexed because the feed list filters and
    /// sorts by it over thousands of rows; a COALESCE in the query could not use an index.
    /// </summary>
    public DateTime SortUtc { get; set; }
}
