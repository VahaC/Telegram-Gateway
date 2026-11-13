using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using AgentGateway.Api.Http;
using AgentGateway.Core.Options;
using AgentGateway.Core.Security;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AgentGateway.Api.Auth;

public static class McpOAuthEndpoints
{
    public static bool IsPublicPath(PathString path) => path.Value is "/oauth/authorize" or "/oauth/token"
        or "/.well-known/openid-configuration" or "/.well-known/oauth-authorization-server"
        or "/.well-known/jwks" or "/.well-known/oauth-protected-resource" or "/.well-known/oauth-protected-resource/mcp";

    public static string CredentialVersion(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static void MapMcpOAuth(this WebApplication app, McpOAuthOptions options)
    {
        object Metadata() => new
        {
            resource = options.Resource, authorization_servers = new[] { options.Issuer },
            scopes_supported = McpOAuthOptions.ResourceScopes, bearer_methods_supported = new[] { "header" }
        };
        app.MapGet("/.well-known/oauth-protected-resource", Metadata);
        app.MapGet("/.well-known/oauth-protected-resource/mcp", Metadata);
        app.MapMethods("/oauth/authorize", ["GET", "POST"], AuthorizeAsync).DisableAntiforgery();
        app.MapPost("/oauth/token", ExchangeAsync).DisableAntiforgery();
    }

    private static async Task<IResult> AuthorizeAsync(HttpContext context, IAntiforgery antiforgery,
        IOptions<GatewayOptions> gateway, IOptions<McpOAuthOptions> oauth)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("OAuth request is missing.");
        var granted = GrantedScopes(request.GetScopes(), request.ClientId, oauth.Value);
        if (!granted.Any(McpOAuthOptions.ResourceScopes.Contains) || !ValidResource(request, oauth.Value))
            return Deny(Errors.InvalidTarget, "The requested resource or scope is not allowed.");
        if (HttpMethods.IsPost(context.Request.Method))
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return ApiErrors.Error(400, "Invalid authorization form."); }
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            if (form["action"] != "allow") return Deny(Errors.AccessDenied, "Authorization was declined.");
            var credential = form["accessKey"];
            if (credential.Count != 1 || credential[0] is not { Length: <= 256 } key || !ApiKeyComparison.Matches(key, gateway.Value.ApiKey))
                return ApiErrors.Error(401, "Authentication required.");
            var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            identity.SetClaim(Claims.Subject, "gateway-owner");
            identity.SetClaim("credential_version", CredentialVersion(gateway.Value.ApiKey));
            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(granted);
            principal.SetResources(oauth.Value.Resource);
            principal.SetDestinations(claim => [Destinations.AccessToken]);
            return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
        var tokens = antiforgery.GetAndStoreTokens(context);
        var encoder = HtmlEncoder.Default;
        var hiddenParameters = string.Concat(new[] { "response_type", "client_id", "redirect_uri", "scope", "state", "resource", "code_challenge", "code_challenge_method" }
            .Where(name => request.HasParameter(name))
            .Select(name => $"<input type=\"hidden\" name=\"{name}\" value=\"{encoder.Encode(request.GetParameter(name).ToString() ?? "")}\">"));
        var abilities = string.Concat(granted.Where(McpOAuthOptions.ResourceScopes.Contains).Select(scope => scope == McpOAuthOptions.NewsScope
            ? "<li>submit news issues for publication on the website and read the news registry</li>"
            : "<li>send notifications to the configured Telegram chat and read delivery metadata</li>"));
        var html = "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Authorize AgentGateway access</title></head><body>"
            + $"<h1>Authorize AgentGateway access</h1><p>Client <b>{encoder.Encode(request.ClientId ?? "")}</b> will be able to:</p><ul>{abilities}</ul>"
            + "<p>Enter the gateway access key to grant access. Do not enter a Telegram bot token.</p>"
            + $"<form method=\"post\" action=\"{encoder.Encode(context.Request.Path)}\">" + hiddenParameters
            + $"<input type=\"hidden\" name=\"{encoder.Encode(tokens.FormFieldName)}\" value=\"{encoder.Encode(tokens.RequestToken!)}\">"
            + "<label>Gateway access key <input type=\"password\" name=\"accessKey\" required maxlength=\"256\" autocomplete=\"off\"></label>"
            + "<p><button type=\"submit\" name=\"action\" value=\"allow\">Allow access</button> <button type=\"submit\" name=\"action\" value=\"deny\" formnovalidate>Cancel</button></p></form></body></html>";
        // Browsers enforce form-action on the POST's OAuth redirect too; OpenIddict has validated this exact callback.
        var callback = new Uri(request.RedirectUri!).GetLeftPart(UriPartial.Path);
        context.Response.Headers.ContentSecurityPolicy = $"default-src 'none'; form-action 'self' {callback}; frame-ancestors 'none'; base-uri 'none'";
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static async Task<IResult> ExchangeAsync(HttpContext context, IOptions<GatewayOptions> gateway, IOptions<McpOAuthOptions> oauth)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("OAuth request is missing.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            return Deny(Errors.UnsupportedGrantType, "This grant is not supported.");
        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (result.Principal is not { } principal || principal.GetClaim(Claims.Subject) != "gateway-owner"
            || principal.GetClaim("credential_version") != CredentialVersion(gateway.Value.ApiKey)
            || !ValidResource(request, oauth.Value))
            return Deny(Errors.InvalidGrant, "Authorization is no longer valid.");
        // A scope removed from the client's configuration stops being renewed by refresh tokens.
        var scopes = GrantedScopes(principal.GetScopes(), request.ClientId, oauth.Value);
        if (!scopes.Any(McpOAuthOptions.ResourceScopes.Contains)) return Deny(Errors.InvalidGrant, "Authorization is no longer valid.");
        principal.SetScopes(scopes);
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// The requested scopes narrowed to what this client was configured with (RFC 6749 3.3 allows a narrower grant).
    /// ChatGPT keeps telegram:send even if it starts asking for every advertised scope; openid/offline_access pass through.
    /// </summary>
    private static string[] GrantedScopes(IEnumerable<string> requested, string? clientId, McpOAuthOptions options)
    {
        var client = options.Clients.FirstOrDefault(entry => entry.ClientId == clientId);
        return client is null ? [] : requested.Where(scope => !McpOAuthOptions.ResourceScopes.Contains(scope) || client.Scopes.Contains(scope))
            .Distinct().ToArray();
    }

    private static bool ValidResource(OpenIddictRequest request, McpOAuthOptions options)
    {
        // The resource is fixed even for clients omitting RFC 8707; explicit mismatches are rejected.
        var resources = request.GetResources();
        return resources.Length == 0 || resources.Length == 1 && resources[0] == options.Resource;
    }

    private static IResult Deny(string error, string description) => Results.Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        }), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
}
