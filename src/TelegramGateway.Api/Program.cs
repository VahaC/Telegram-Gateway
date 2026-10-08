using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TelegramGateway.Api.Extensions;
using TelegramGateway.Api.Http;
using TelegramGateway.Api.Serialization;
using TelegramGateway.Api.Services.Delivery;
using TelegramGateway.Core.Options;
using TelegramGateway.Core.Security;
using TelegramGateway.Infrastructure.Persistence;
using TelegramGateway.Api.Auth;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace TelegramGateway.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddEnvironmentVariables("TELEGRAMGATEWAY_");
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 128 * 1024);
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole();
        // EF command logging can contain private data; outbound client logging is removed at registration.
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);
        builder.Logging.AddFilter("ModelContextProtocol", LogLevel.Warning);
        builder.Logging.AddFilter("OpenIddict", LogLevel.None); // Protocol logs may contain token or authorization-code values.
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.Converters.Add(new UtcDateTimeConverter());
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        });
        builder.Services.AddGateway(builder.Configuration);
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        var application = builder.Build();
        var settings = application.Services.GetRequiredService<IOptions<GatewayOptions>>().Value;
        var oauth = application.Services.GetRequiredService<IOptions<McpOAuthOptions>>().Value;
        Directory.CreateDirectory(settings.DataPath);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(settings.DataPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        // A single process owns crash recovery and chat ordering; multiple replicas must fail closed.
        using var workerLock = new FileStream(Path.Combine(settings.DataPath, ".worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        application.Lifetime.ApplicationStopped.Register(workerLock.Dispose);
        using (var scope = application.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await database.Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<DeliveryReconciler>().ReconcileAsync(CancellationToken.None);
        }
        await application.Services.InitializeMcpOAuthAsync(settings);
        if (settings.KnownProxies.Length > 0) application.UseForwardedHeaders();
        application.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var status = exception is BadHttpRequestException badRequest ? badRequest.StatusCode : 500;
            await ApiErrors.Error(status, status >= 500 ? $"Request failed. Trace: {context.TraceIdentifier}." : "Invalid request body.").ExecuteAsync(context);
        }));
        application.Use(async (context, next) =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.XFrameOptions = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            context.Response.Headers.CacheControl = "no-store";
            if (!settings.AllowedHosts.Contains(context.Request.Host.Host, StringComparer.OrdinalIgnoreCase))
            {
                await ApiErrors.Error(400, "Host is not allowed.").ExecuteAsync(context);
                return;
            }
            // TestServer also traverses the explicit size guard; Kestrel provides the streaming limit in production.
            if (context.Request.ContentLength > 128 * 1024)
            {
                await ApiErrors.Error(413, "Request body is too large.").ExecuteAsync(context);
                return;
            }
            if (context.Request.Path.StartsWithSegments("/mcp") && context.Request.Headers.Origin is { Count: > 0 } origins
                && (!Uri.TryCreate(origins.ToString(), UriKind.Absolute, out var origin)
                    || !settings.AllowedHosts.Contains(origin.Host, StringComparer.OrdinalIgnoreCase)))
            {
                await ApiErrors.Error(403, "Origin is not allowed.").ExecuteAsync(context);
                return;
            }
            await next(context);
        });
        application.UseRateLimiter();
        application.Use(async (context, next) =>
        {
            if (!oauth.Enabled && McpOAuthEndpoints.IsPublicPath(context.Request.Path))
            {
                await ApiErrors.Error(401, "Authentication required.").ExecuteAsync(context);
                return;
            }
            await next(context);
        });
        application.UseAuthentication();
        application.Use(async (context, next) =>
        {
            if (oauth.Enabled && McpOAuthEndpoints.IsPublicPath(context.Request.Path))
            {
                await next(context);
                return;
            }
            if (oauth.Enabled && context.Request.Path.StartsWithSegments("/mcp") && !context.Request.Headers.ContainsKey("X-Api-Key"))
            {
                var authentication = await context.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                var principal = authentication.Principal;
                if (!authentication.Succeeded || principal is null || !principal.HasScope(McpOAuthOptions.Scope)
                    || principal.GetClaim(OpenIddictConstants.Claims.Subject) != "gateway-owner"
                    || principal.GetClaim("credential_version") != McpOAuthEndpoints.CredentialVersion(settings.ApiKey))
                {
                    context.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{oauth.Issuer}.well-known/oauth-protected-resource\", scope=\"{McpOAuthOptions.Scope}\"";
                    await ApiErrors.Error(401, "Authentication required.").ExecuteAsync(context);
                    return;
                }
                context.User = principal;
                await next(context);
                return;
            }
            if (context.Request.Path is not { Value: "/api/health" or "/api/ready" }
                && (!context.Request.Headers.TryGetValue("X-Api-Key", out var credential) || credential.Count != 1
                    || credential[0] is not { Length: <= 256 } supplied || !ApiKeyComparison.Matches(supplied, settings.ApiKey)))
            {
                await ApiErrors.Error(401, "Authentication required.").ExecuteAsync(context);
                return;
            }
            await next(context);
        });
        application.MapGatewayEndpoints();
        if (oauth.Enabled) application.MapMcpOAuth(oauth);
        if (settings.EnableMcp) application.MapMcp("/mcp");
        await application.RunAsync();
    }
}
