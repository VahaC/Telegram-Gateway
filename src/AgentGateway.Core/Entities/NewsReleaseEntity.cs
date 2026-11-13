namespace AgentGateway.Core.Entities;

public class NewsReleaseEntity : AuditableEntity
{
    public Guid IssueId { get; set; }
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Url { get; set; } = "";
    public string CanonicalUrl { get; set; } = "";
    public string? Note { get; set; }
}
