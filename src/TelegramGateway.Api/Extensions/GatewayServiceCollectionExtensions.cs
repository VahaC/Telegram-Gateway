using System.Net;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TelegramGateway.Api.Contracts;
using TelegramGateway.Api.Http;
using TelegramGateway.Api.Mapping;
using TelegramGateway.Api.Mcp;
using TelegramGateway.Api.Services.Delivery;
using TelegramGateway.Api.Validators;
using TelegramGateway.Core.Options;
using TelegramGateway.Infrastructure;

namespace TelegramGateway.Api.Extensions;

public static class GatewayServiceCollectionExtensions
{
    public static IServiceCollection AddGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GatewayOptions>().Bind(configuration.GetSection(GatewayOptions.SectionName))
            .Configure(options =>
            {
                options.BotToken = configuration["TELEGRAM_BOT_TOKEN"] ?? options.BotToken;
                options.ChatId = configuration["TELEGRAM_CHAT_ID"] ?? options.ChatId;
                options.ApiKey = configuration["API_KEY"] ?? options.ApiKey;
                options.KnownProxies = options.KnownProxies.Where(proxy => !string.IsNullOrWhiteSpace(proxy)).ToArray();
            })
            .Validate(options => Regex.IsMatch(options.BotToken, "^[0-9]+:[A-Za-z0-9_-]+$"), "TELEGRAM_BOT_TOKEN is required and must have a bot token shape.")
            .Validate(options => long.TryParse(options.ChatId, out var chatId) && chatId > 0, "TELEGRAM_CHAT_ID must be a positive private chat identifier.")
            .Validate(options => options.ApiKey.Length is >= 32 and <= 256, "API_KEY must contain 32 to 256 characters.")
            .Validate(options => options.MessageLength is >= 16 and <= 4096 && options.MaxParts is >= 1 and <= 64, "Invalid message limits.")
            .Validate(options => options.MaxAttempts is >= 1 and <= 6 && options.RetryBaseMilliseconds is >= 0 and <= 10000
                && options.MessageIntervalMilliseconds is >= 0 and <= 10000 && options.MaxRetryAfterSeconds is >= 1 and <= 3600, "Invalid retry settings.")
            .Validate(options => options.RequestLimitPerMinute is >= 1 and <= 1000 && !string.IsNullOrWhiteSpace(options.DataPath), "Invalid local settings.")
            .Validate(options => options.KnownProxies.All(proxy => IPAddress.TryParse(proxy, out _)), "Known proxies must be literal IP addresses.")
            .Validate(options => options.AllowedHosts.Length > 0 && options.AllowedHosts.All(host => !string.IsNullOrWhiteSpace(host) && !host.Contains('*') && !host.Contains('/')), "Explicit allowed hostnames are required.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddGatewayInfrastructure();
        services.AddSingleton<IDeliveryMapper, DeliveryMapper>();
        services.AddSingleton<IValidator<MessageRequest>, MessageValidator>();
        services.AddSingleton<IValidator<DigestRequest>, DigestValidator>();
        services.AddScoped<DeliveryService>();
        services.AddScoped<DeliveryProcessor>();
        services.AddScoped<DeliveryReconciler>();
        services.AddHostedService<DeliveryWorker>();
        services.AddOpenApi(options => options.AddDocumentTransformer<TelegramGateway.Api.OpenApi.ApiKeyDocumentTransformer>());
        services.AddMcpServer().WithHttpTransport().WithTools<GatewayTools>();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in (configuration.GetSection("Gateway:KnownProxies").Get<string[]>() ?? []).Where(proxy => !string.IsNullOrWhiteSpace(proxy)))
                options.KnownProxies.Add(IPAddress.Parse(proxy));
        });
        services.AddRateLimiter(options =>
        {
            // All requests, including failed auth, count. Only a trusted proxy may replace RemoteIpAddress.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<GatewayOptions>>().Value.RequestLimitPerMinute,
                    Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await ApiErrors.Error(429, "Request rate limit exceeded.").ExecuteAsync(context.HttpContext);
            };
        });
        return services;
    }
}
