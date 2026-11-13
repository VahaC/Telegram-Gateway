namespace AgentGateway.Core.Options;

public sealed class McpOAuthClientOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string[] RedirectUris { get; set; } = [];
    public string[] Scopes { get; set; } = [];
}
