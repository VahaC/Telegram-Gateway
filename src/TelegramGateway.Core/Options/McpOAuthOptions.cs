namespace TelegramGateway.Core.Options;

public sealed class McpOAuthOptions
{
    public const string SectionName = "McpOAuth";
    public const string Scope = "telegram:send";
    public bool Enabled { get; set; }
    public string PublicUrl { get; set; } = "";
    public string ClientId { get; set; } = "telegram-gateway";
    public string ClientSecret { get; set; } = "";
    public string[] RedirectUris { get; set; } = [];
    public string Issuer => PublicUrl.TrimEnd('/') + "/";
    public string Resource => PublicUrl.TrimEnd('/') + "/mcp";
}
