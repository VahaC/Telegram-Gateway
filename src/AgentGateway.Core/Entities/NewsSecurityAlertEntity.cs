namespace AgentGateway.Core.Entities;

public class NewsSecurityAlertEntity : AuditableEntity
{
    public Guid IssueId { get; set; }
    public int Position { get; set; }
    public string AlertId { get; set; } = "";
    public string Product { get; set; } = "";
    public string Severity { get; set; } = "";
    public double? Cvss { get; set; }
    public DateOnly Date { get; set; }
    public string Url { get; set; } = "";
    public string CanonicalUrl { get; set; } = "";
    public string? Note { get; set; }
}
