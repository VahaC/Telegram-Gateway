using System.Security.Claims;
using OpenIddict.Abstractions;
using AgentGateway.Core.Options;

namespace AgentGateway.Api.Mcp;

/// <summary>
/// Per-tool scope policy: Telegram tools need telegram:send, news tools need news:publish. API-key callers get a principal
/// carrying both scopes, so this one rule covers both kinds of authentication.
/// </summary>
public static class McpToolAccess
{
    public static string RequiredScope(string? tool) => NewsTools.All.Contains(tool) ? McpOAuthOptions.NewsScope : McpOAuthOptions.TelegramScope;

    public static bool Allows(ClaimsPrincipal? user, string? tool, bool newsEnabled)
        => user is not null && user.HasScope(RequiredScope(tool)) && (newsEnabled || !NewsTools.All.Contains(tool));

    public static ClaimsPrincipal ApiKeyPrincipal()
    {
        var identity = new ClaimsIdentity("ApiKey");
        identity.SetClaim(OpenIddictConstants.Claims.Subject, "gateway-owner");
        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(McpOAuthOptions.ResourceScopes);
        return principal;
    }
}
