using AgentGateway.Core.Enums;

namespace AgentGateway.Core.Entities;

/// <summary>A post that a replaced issue no longer contains; the row is deleted once WordPress confirms the removal.</summary>
public class NewsRemovalEntity : AuditableEntity
{
    public Guid IssueId { get; set; }
    public string EventKey { get; set; } = "";
    public NewsItemStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? ErrorCode { get; set; }
}
