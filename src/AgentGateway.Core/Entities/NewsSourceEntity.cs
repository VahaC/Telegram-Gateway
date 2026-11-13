namespace AgentGateway.Core.Entities;

public class NewsSourceEntity : AuditableEntity
{
    public Guid ItemId { get; set; }
    public int Position { get; set; }
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    public string CanonicalUrl { get; set; } = "";
}
