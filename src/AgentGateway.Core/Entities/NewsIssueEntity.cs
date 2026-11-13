using AgentGateway.Core.Enums;

namespace AgentGateway.Core.Entities;

public class NewsIssueEntity : AuditableEntity
{
    public DateOnly Date { get; set; }
    public string Title { get; set; } = "";
    public string Language { get; set; } = "";
    public string? Intro { get; set; }
    public string? ResearchMode { get; set; }
    public int? ResearchSearchPasses { get; set; }
    public int? ResearchCandidates { get; set; }
    public int? ResearchDuplicatesRemoved { get; set; }
    public int? ResearchWindowHours { get; set; }
    /// <summary>The accepted issue document in its canonical JSON form; every WordPress body is derived from it.</summary>
    public string Document { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public int Revision { get; set; } = 1;
    public DateTime SubmittedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public NewsPublicationStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? ErrorCode { get; set; }
    public long? DayPostId { get; set; }
    public string? DayUrl { get; set; }
    /// <summary>The revision WordPress last confirmed for the day post; a smaller value means the day must be sent again.</summary>
    public int? DayRevision { get; set; }
    public DateTime? PublishedUtc { get; set; }
    public Guid? NoticeDeliveryId { get; set; }
    public int? NoticeRevision { get; set; }
    /// <summary>Set when the test fixture is being removed: the worker deletes its item posts, then the issue row.</summary>
    public DateTime? DeleteRequestedUtc { get; set; }
    public List<NewsItemEntity> Items { get; set; } = [];
    public List<NewsReleaseEntity> Releases { get; set; } = [];
    public List<NewsSecurityAlertEntity> SecurityAlerts { get; set; } = [];
    public List<NewsRemovalEntity> Removals { get; set; } = [];
}
