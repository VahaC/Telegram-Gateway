using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation;
using AgentGateway.Api.Auth;
using AgentGateway.Core.Options;
using AgentGateway.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AgentGateway.Api.Extensions;

public static class McpOAuthExtensions
{
    public static IServiceCollection AddMcpOAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<McpOAuthOptions>().Bind(configuration.GetSection(McpOAuthOptions.SectionName))
            .Configure(options =>
            {
                options.ClientSecret = configuration["OAUTH_CLIENT_SECRET"] ?? options.ClientSecret;
                options.Clients = BuildClients(configuration, options);
            })
            .Validate(options => !options.Enabled || IsHttpsOrigin(options.PublicUrl), "OAuth PublicUrl must be an HTTPS origin without credentials, query or fragment.")
            .Validate(options => !options.Enabled || options.Clients.Count > 0, "OAuth requires at least one client.")
            .Validate(options => !options.Enabled || options.Clients.All(client => client.ClientSecret.Length is >= 32 and <= 256), "OAuth ClientSecret must contain 32 to 256 characters for every client.")
            .Validate(options => !options.Enabled || options.Clients.All(client => client.ClientId.Length is >= 1 and <= 100)
                && options.Clients.Select(client => client.ClientId).Distinct(StringComparer.Ordinal).Count() == options.Clients.Count, "OAuth ClientId must contain 1 to 100 characters and be unique.")
            .Validate(options => !options.Enabled || options.Clients.All(client => client.RedirectUris.Length > 0 && client.RedirectUris.All(IsHttpsRedirect)), "OAuth requires explicit HTTPS redirect URIs without credentials or fragments for every client.")
            .Validate(options => !options.Enabled || options.Clients.All(client => client.Scopes.Length > 0 && client.Scopes.All(McpOAuthOptions.ResourceScopes.Contains)),
                $"OAuth client scopes must be {McpOAuthOptions.TelegramScope} and/or {McpOAuthOptions.NewsScope}.")
            .ValidateOnStart();
        services.AddDbContext<OAuthDbContext>((provider, options) =>
        {
            var settings = provider.GetRequiredService<IOptions<GatewayOptions>>().Value;
            options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(settings.DataPath, "oauth.db"), DefaultTimeout = 5
            }.ToString(), sqlite => sqlite.MigrationsAssembly("AgentGateway.Api"));
            options.UseOpenIddict();
        });
        services.AddAuthentication();
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "TelegramGateway.Antiforgery"; // Pre-rename names kept: consent forms open during an upgrade stay valid.
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        // The application name is part of the key derivation; renaming it would orphan the protected keys in .secrets.
        services.AddDataProtection().SetApplicationName("TelegramGateway.OAuth");
        services.AddOptions<KeyManagementOptions>().Configure<IOptions<GatewayOptions>, IOptions<McpOAuthOptions>, TimeProvider>((options, gateway, oauth, time) =>
        {
            if (!oauth.Value.Enabled) return;
            var path = Path.Combine(gateway.Value.DataPath, ".secrets", "data-protection");
            Directory.CreateDirectory(path);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            options.XmlRepository = new Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository(new DirectoryInfo(path), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
            options.XmlEncryptor = new Microsoft.AspNetCore.DataProtection.XmlEncryption.CertificateXmlEncryptor(OAuthKeyMaterial.Load(gateway.Value.DataPath, time), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        });
        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<OAuthDbContext>())
            .AddServer(options =>
            {
                options.SetAuthorizationEndpointUris("oauth/authorize").SetTokenEndpointUris("oauth/token");
                options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
                options.RegisterScopes(McpOAuthOptions.TelegramScope, McpOAuthOptions.NewsScope, Scopes.OfflineAccess);
                options.RequireProofKeyForCodeExchange();
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(30));
                options.SetRefreshTokenReuseLeeway(TimeSpan.Zero);
                options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(2));
                options.AddEventHandler<OpenIddictServerEvents.HandleConfigurationRequestContext>(handler => handler.UseInlineHandler(context =>
                {
                    context.Metadata["authorization_response_iss_parameter_supported"] = true;
                    return default;
                }));
                options.AddEventHandler<OpenIddictServerEvents.ApplyAuthorizationResponseContext>(handler => handler.SetOrder(int.MinValue + 100000).UseInlineHandler(context =>
                {
                    context.Response.SetParameter("iss", context.Options.Issuer!.AbsoluteUri);
                    return default;
                }));
                options.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
                options.EnableTokenEntryValidation();
            });
        services.AddOptions<OpenIddictServerOptions>().Configure<IOptions<GatewayOptions>, IOptions<McpOAuthOptions>, TimeProvider>((options, gateway, oauth, time) =>
        {
            options.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain);
            if (!oauth.Value.Enabled)
            {
                var ephemeral = new Microsoft.IdentityModel.Tokens.RsaSecurityKey(System.Security.Cryptography.RSA.Create(2048));
                options.SigningCredentials.Add(new Microsoft.IdentityModel.Tokens.SigningCredentials(ephemeral, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaSha256));
                options.EncryptionCredentials.Add(new Microsoft.IdentityModel.Tokens.EncryptingCredentials(ephemeral, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaOAEP, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.Aes256CbcHmacSha512));
                return;
            }
            options.Issuer = new Uri(oauth.Value.Issuer);
            options.Resources.Add(new Uri(oauth.Value.Resource));
            // OpenIddict keeps the private certificate for the host lifetime.
            var key = new Microsoft.IdentityModel.Tokens.X509SecurityKey(OAuthKeyMaterial.Load(gateway.Value.DataPath, time));
            options.SigningCredentials.Add(new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaSha256));
            options.EncryptionCredentials.Add(new Microsoft.IdentityModel.Tokens.EncryptingCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaOAEP, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.Aes256CbcHmacSha512));
        });
        services.AddOptions<OpenIddictValidationOptions>().Configure<IOptions<McpOAuthOptions>>((options, oauth) =>
        {
            if (oauth.Value.Enabled) options.Audiences.Add(oauth.Value.Resource);
        });
        return services;
    }

    public static async Task InitializeMcpOAuthAsync(this IServiceProvider services, GatewayOptions gateway)
    {
        var options = services.GetRequiredService<IOptions<McpOAuthOptions>>().Value;
        if (!options.Enabled) return;
        if (!gateway.EnableMcp) throw new InvalidOperationException("OAuth requires EnableMcp=true.");
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OAuthDbContext>().Database.MigrateAsync();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        foreach (var client in options.Clients)
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = client.ClientId, ClientSecret = client.ClientSecret,
                DisplayName = "AgentGateway client " + client.ClientId, ClientType = ClientTypes.Confidential,
                ConsentType = ConsentTypes.Explicit
            };
            foreach (var redirect in client.RedirectUris) descriptor.RedirectUris.Add(new Uri(redirect));
            // Every client may ask for both resource scopes, so a client that asks for more than it was given still gets a
            // token; the authorization endpoint narrows the grant to the client's configured scopes (McpOAuthEndpoints).
            descriptor.Permissions.UnionWith([Permissions.Endpoints.Authorization, Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code, Permissions.Prefixes.Scope + McpOAuthOptions.TelegramScope,
                Permissions.Prefixes.Scope + McpOAuthOptions.NewsScope, Permissions.Prefixes.Resource + options.Resource]);
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
            var application = await manager.FindByClientIdAsync(client.ClientId);
            if (application is null) await manager.CreateAsync(descriptor);
            else await manager.UpdateAsync(application, descriptor);
        }
        // A client removed from configuration must stop working, together with its tokens.
        var configured = options.Clients.Select(client => client.ClientId).ToHashSet(StringComparer.Ordinal);
        var stale = new List<object>();
        await foreach (var application in manager.ListAsync())
            if (!configured.Contains(await manager.GetClientIdAsync(application) ?? "")) stale.Add(application);
        foreach (var application in stale) await manager.DeleteAsync(application);
    }

    /// <summary>
    /// Client 0 keeps the original single-client keys (McpOAuth:ClientId, OAUTH_CLIENT_SECRET, McpOAuth:RedirectUris) and the
    /// telegram:send scope, so an existing installation needs no change. Clients 1+ come from McpOAuth:Clients:{n}; a blank
    /// ClientId skips the entry, which lets Compose pass empty placeholders.
    /// </summary>
    private static List<McpOAuthClientOptions> BuildClients(IConfiguration configuration, McpOAuthOptions options)
    {
        var entries = configuration.GetSection(McpOAuthOptions.SectionName + ":Clients").GetChildren()
            .Where(child => int.TryParse(child.Key, out _)).OrderBy(child => int.Parse(child.Key, System.Globalization.CultureInfo.InvariantCulture))
            .ToDictionary(child => child.Key, child => child.Get<McpOAuthClientOptions>() ?? new McpOAuthClientOptions());
        var first = entries.GetValueOrDefault("0") ?? new McpOAuthClientOptions();
        entries["0"] = new McpOAuthClientOptions
        {
            ClientId = Fallback(first.ClientId, options.ClientId),
            ClientSecret = Fallback(first.ClientSecret, options.ClientSecret),
            RedirectUris = Clean(first.RedirectUris) is { Length: > 0 } redirects ? redirects : Clean(options.RedirectUris),
            Scopes = Clean(first.Scopes) is { Length: > 0 } scopes ? scopes : [McpOAuthOptions.TelegramScope]
        };
        return entries.OrderBy(entry => int.Parse(entry.Key, System.Globalization.CultureInfo.InvariantCulture)).Select(entry => entry.Value)
            .Where(client => !string.IsNullOrWhiteSpace(client.ClientId))
            .Select(client => new McpOAuthClientOptions
            {
                ClientId = client.ClientId.Trim(), ClientSecret = client.ClientSecret, RedirectUris = Clean(client.RedirectUris),
                Scopes = Clean(client.Scopes)
            }).ToList();
    }

    private static string Fallback(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string[] Clean(string[] values) => values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray();

    private static bool IsHttpsOrigin(string value) => IsHttpsRedirect(value)
        && new Uri(value).AbsolutePath == "/" && new Uri(value).Query.Length == 0;
    private static bool IsHttpsRedirect(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
}
