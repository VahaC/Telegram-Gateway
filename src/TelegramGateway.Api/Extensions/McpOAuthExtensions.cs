using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation;
using TelegramGateway.Api.Auth;
using TelegramGateway.Core.Options;
using TelegramGateway.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace TelegramGateway.Api.Extensions;

public static class McpOAuthExtensions
{
    public static IServiceCollection AddMcpOAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<McpOAuthOptions>().Bind(configuration.GetSection(McpOAuthOptions.SectionName))
            .Configure(options => options.ClientSecret = configuration["OAUTH_CLIENT_SECRET"] ?? options.ClientSecret)
            .Validate(options => !options.Enabled || IsHttpsOrigin(options.PublicUrl), "OAuth PublicUrl must be an HTTPS origin without credentials, query or fragment.")
            .Validate(options => !options.Enabled || options.ClientSecret.Length is >= 32 and <= 256, "OAuth ClientSecret must contain 32 to 256 characters.")
            .Validate(options => !options.Enabled || options.ClientId.Length is >= 1 and <= 100, "OAuth ClientId must contain 1 to 100 characters.")
            .Validate(options => !options.Enabled || options.RedirectUris.Length > 0 && options.RedirectUris.All(IsHttpsRedirect), "OAuth requires explicit HTTPS redirect URIs without credentials or fragments.")
            .ValidateOnStart();
        services.AddDbContext<OAuthDbContext>((provider, options) =>
        {
            var settings = provider.GetRequiredService<IOptions<GatewayOptions>>().Value;
            options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(settings.DataPath, "oauth.db"), DefaultTimeout = 5
            }.ToString(), sqlite => sqlite.MigrationsAssembly("TelegramGateway.Api"));
            options.UseOpenIddict();
        });
        services.AddAuthentication();
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "TelegramGateway.Antiforgery";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
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
                options.RegisterScopes(McpOAuthOptions.Scope, Scopes.OfflineAccess);
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
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = options.ClientId, ClientSecret = options.ClientSecret,
            DisplayName = "Telegram notification client", ClientType = ClientTypes.Confidential,
            ConsentType = ConsentTypes.Explicit
        };
        foreach (var redirect in options.RedirectUris) descriptor.RedirectUris.Add(new Uri(redirect));
        descriptor.Permissions.UnionWith([Permissions.Endpoints.Authorization, Permissions.Endpoints.Token,
            Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
            Permissions.ResponseTypes.Code, Permissions.Prefixes.Scope + McpOAuthOptions.Scope,
            Permissions.Prefixes.Resource + options.Resource]);
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        var application = await manager.FindByClientIdAsync(options.ClientId);
        if (application is null) await manager.CreateAsync(descriptor);
        else await manager.UpdateAsync(application, descriptor);
    }

    private static bool IsHttpsOrigin(string value) => IsHttpsRedirect(value)
        && new Uri(value).AbsolutePath == "/" && new Uri(value).Query.Length == 0;
    private static bool IsHttpsRedirect(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
}
