using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using AgentGateway.Api.Contracts;
using AgentGateway.Api.Http;
using AgentGateway.Api.Mapping;
using AgentGateway.Api.Mcp;
using AgentGateway.Api.Services.Delivery;
using AgentGateway.Api.Services.News;
using AgentGateway.Api.Contracts.News;
using AgentGateway.Api.Validators;
using AgentGateway.Core.Options;
using AgentGateway.Infrastructure;

namespace AgentGateway.Api.Extensions;

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
        services.AddOptions<NewsOptions>().Bind(configuration.GetSection(NewsOptions.SectionName))
            .Configure(options => options.WordPressAppPassword = configuration["WORDPRESS_APP_PASSWORD"] ?? options.WordPressAppPassword)
            .Validate(options => !options.Enabled || Uri.TryCreate(options.WordPressBaseUrl, UriKind.Absolute, out var uri)
                && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Query.Length == 0,
                "News:WordPressBaseUrl must be an https origin such as https://vahac.com.")
            .Validate(options => !options.Enabled || options.WordPressUser.Length is >= 1 and <= 60 && !options.WordPressUser.Contains(':'),
                "News:WordPressUser is required when News:Enabled=true (the WordPress user with the vahac_news_publisher role).")
            .Validate(options => !options.Enabled || options.WordPressAppPassword.Length is >= 16 and <= 256,
                "WORDPRESS_APP_PASSWORD is required when News:Enabled=true (an Application Password of the news user).")
            .Validate(options => options.MinItems is >= 1 and <= 80 && options.MaxItems is >= 1 and <= 80 && options.MinItems <= options.MaxItems
                && options.MinPerCategory is >= 0 and <= 80 && options.MaxAiShare is >= 0 and <= 1, "Invalid news editorial settings.")
            .Validate(options => options.CallIntervalMilliseconds is >= 0 and <= 60000 && options.MaxAttempts is >= 1 and <= 10
                && options.RetryBaseMilliseconds is >= 0 and <= 60000 && options.ItemTimeoutSeconds is >= 1 and <= 600, "Invalid news publication settings.")
            .Validate(options => options.FeedPollMinutes is >= 5 and <= 1440 && options.FeedParallelism is >= 1 and <= 16
                && !string.IsNullOrWhiteSpace(options.FeedCatalogPath),
                "News:FeedPollMinutes must be 5-1440, News:FeedParallelism 1-16 and News:FeedCatalogPath set (default NewsFeeds/news-feeds.json).")
            // ADR 0004: the collector introduces itself as a feed reader; a browser token would disguise it as a person.
            .Validate(options => options.FeedUserAgent.Length is >= 1 and <= 200
                && !options.FeedUserAgent.Contains("Mozilla", StringComparison.OrdinalIgnoreCase)
                && new HttpRequestMessage().Headers.UserAgent.TryParseAdd(options.FeedUserAgent),
                "News:FeedUserAgent must be a valid User-Agent of at most 200 characters naming the gateway, not a browser, "
                + "e.g. AgentGateway-NewsFeed/1.0 (personal feed reader).")
            .Validate(options => !options.Enabled || !options.WatchdogEnabled
                || TimeOnly.TryParseExact(options.WatchdogTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "News:WatchdogTime must be a 24-hour HH:mm time such as 06:00.")
            .Validate(options => !options.Enabled || !options.WatchdogEnabled
                || TimeZoneInfo.TryFindSystemTimeZoneById(options.WatchdogTimeZone ?? "", out _),
                "News:WatchdogTimeZone must be an IANA time zone such as UTC or Europe/Kyiv.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddMcpOAuth(configuration);
        services.AddGatewayInfrastructure();
        services.AddSingleton<IDeliveryMapper, DeliveryMapper>();
        services.AddSingleton<IValidator<MessageRequest>, MessageValidator>();
        services.AddSingleton<IValidator<DigestRequest>, DigestValidator>();
        services.AddScoped<DeliveryService>();
        services.AddScoped<DeliveryProcessor>();
        services.AddScoped<DeliveryReconciler>();
        services.AddHostedService<DeliveryWorker>();
        services.AddSingleton<INewsMapper, NewsMapper>();
        services.AddSingleton<IValidator<NewsIssueRequest>, NewsIssueValidator>();
        services.AddSingleton<NewsEditorialGate>();
        services.AddScoped<NewsDuplicateFinder>();
        services.AddScoped<NewsService>();
        services.AddScoped<NewsPublicationProcessor>();
        services.AddScoped<NewsPublicationReconciler>();
        services.AddHostedService<NewsPublicationWorker>();
        // ADR 0004: the feed collector and the watchdog are separate loops, so a slow poll can never delay the morning check.
        services.AddScoped<NewsFeedCatalogSync>();
        services.AddScoped<NewsFeedPoller>();
        services.AddScoped<NewsFeedService>();
        services.AddHostedService<NewsFeedWorker>();
        services.AddScoped<NewsWatchdog>();
        services.AddHostedService<NewsWatchdogWorker>();
        services.AddOpenApi(options => options.AddDocumentTransformer<AgentGateway.Api.OpenApi.ApiKeyDocumentTransformer>());
        services.AddMcpServer().WithHttpTransport().WithTools<GatewayTools>().WithTools<NewsTools>()
            .WithRequestFilters(filters =>
            {
                // Scope enforcement per tool: a telegram:send token neither sees nor calls the news tools, and vice versa.
                filters.AddListToolsFilter(next => async (context, cancellationToken) =>
                {
                    var result = await next(context, cancellationToken);
                    var newsEnabled = context.Services!.GetRequiredService<IOptions<NewsOptions>>().Value.Enabled;
                    result.Tools = result.Tools.Where(tool => McpToolAccess.Allows(context.User, tool.Name, newsEnabled)).ToList();
                    return result;
                });
                filters.AddCallToolFilter(next => async (context, cancellationToken) =>
                {
                    var newsEnabled = context.Services!.GetRequiredService<IOptions<NewsOptions>>().Value.Enabled;
                    if (McpToolAccess.Allows(context.User, context.Params?.Name, newsEnabled)) return await next(context, cancellationToken);
                    return new CallToolResult
                    {
                        IsError = true,
                        Content = [new TextContentBlock { Text = $"This tool is not available to this client: it needs the {McpToolAccess.RequiredScope(context.Params?.Name)} scope"
                            + (newsEnabled ? "." : " and the news module, which is disabled on this gateway.") }]
                    };
                });
            });
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
