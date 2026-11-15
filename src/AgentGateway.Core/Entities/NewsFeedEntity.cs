namespace AgentGateway.Core.Entities;

/// <summary>One RSS/Atom feed of the catalog and the state of its last polls (ADR 0004).</summary>
public class NewsFeedEntity : AuditableEntity
{
    /// <summary>The catalog's stable kebab-case id; the row survives a rename or a new URL of the feed.</summary>
    public string CatalogId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>Category hints (dotnet, homelab, …); an entry of this feed may still belong to another category.</summary>
    public List<string> Categories { get; set; } = [];
    public string Kind { get; set; } = "";
    public string Language { get; set; } = "";
    /// <summary>False once the feed left the catalog; its entries are kept until they age out.</summary>
    public bool Enabled { get; set; }
    /// <summary>Validators of the last 200 answer, sent back as a conditional GET so an unchanged feed costs the publisher a 304.</summary>
    public string? ETag { get; set; }
    public string? LastModified { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public DateTime? LastSuccessUtc { get; set; }
    public int ConsecutiveFailures { get; set; }
    /// <summary>A short code built by the gateway (http_403, timeout, xml_invalid …), never the publisher's text.</summary>
    public string? LastError { get; set; }
}
