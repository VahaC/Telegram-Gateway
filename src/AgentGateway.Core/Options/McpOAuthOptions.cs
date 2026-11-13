namespace AgentGateway.Core.Options;

public sealed class McpOAuthOptions
{
    public const string SectionName = "McpOAuth";
    public const string TelegramScope = "telegram:send";
    public const string NewsScope = "news:publish";
    public static readonly string[] ResourceScopes = [TelegramScope, NewsScope];
    public bool Enabled { get; set; }
    public string PublicUrl { get; set; } = "";
    // The single-client keys predate the client list; they stay supported and become client 0.
    public string ClientId { get; set; } = "telegram-gateway";
    public string ClientSecret { get; set; } = "";
    public string[] RedirectUris { get; set; } = [];
    /// <summary>Effective clients, built from <c>McpOAuth:Clients:{n}</c> plus the single-client keys as client 0.</summary>
    public List<McpOAuthClientOptions> Clients { get; set; } = [];
    public string Issuer => PublicUrl.TrimEnd('/') + "/";
    public string Resource => PublicUrl.TrimEnd('/') + "/mcp";
}
