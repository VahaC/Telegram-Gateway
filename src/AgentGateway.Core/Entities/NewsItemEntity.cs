using AgentGateway.Core.Enums;

namespace AgentGateway.Core.Entities;

public class NewsItemEntity : AuditableEntity
{
    public Guid IssueId { get; set; }
    public int Number { get; set; }
    public string Category { get; set; } = "";
    public bool IsTop { get; set; }
    public int Importance { get; set; }
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string WhyItMatters { get; set; } = "";
    public DateOnly PublishedOn { get; set; }
    public DateOnly? EventOn { get; set; }
    public string? ReleaseStatus { get; set; }
    public string? Version { get; set; }
    public string EventKey { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string CanonicalUrl { get; set; } = "";
    public string? ImageUrl { get; set; }
    public string? ImageAlt { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<NewsSourceEntity> ExtraSources { get; set; } = [];
    public NewsItemStatus Status { get; set; }
    public int? PublishedRevision { get; set; }
    public long? WpPostId { get; set; }
    public string? WpUrl { get; set; }
    public string? ImageResult { get; set; }
    public string? ImageError { get; set; }
    public int Attempts { get; set; }
    public string? ErrorCode { get; set; }
}
