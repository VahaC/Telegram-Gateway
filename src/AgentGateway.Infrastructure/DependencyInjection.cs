using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AgentGateway.Core.Abstractions;
using AgentGateway.Core.Options;
using AgentGateway.Infrastructure.Formatting;
using AgentGateway.Infrastructure.Persistence;
using AgentGateway.Infrastructure.Telegram;
using AgentGateway.Infrastructure.WordPress;

namespace AgentGateway.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGatewayInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<SqlitePragmaInterceptor>();
        services.AddDbContext<ApplicationDbContext>((provider, options) =>
        {
            var settings = provider.GetRequiredService<IOptions<GatewayOptions>>().Value;
            var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(settings.DataPath, "gateway.db"), DefaultTimeout = 5
            }.ToString();
            options.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly("AgentGateway.Api"));
            options.AddInterceptors(provider.GetRequiredService<SqlitePragmaInterceptor>());
        });
        services.AddSingleton<IMessageFormatter, TelegramMessageFormatter>();
        services.AddSingleton<ITelegramClient, TelegramClient>();
        services.AddHttpClient(TelegramClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.telegram.org/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.MaxResponseContentBufferSize = 64 * 1024;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AgentGateway/0.0.0-dev");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
          .RemoveAllLoggers(); // The Telegram token is in the URL; default HttpClient logging would disclose it.
        services.AddSingleton<IWordPressNewsClient, WordPressNewsClient>();
        services.AddHttpClient(WordPressNewsClient.HttpClientName, (provider, client) =>
        {
            var news = provider.GetRequiredService<IOptions<NewsOptions>>().Value;
            client.BaseAddress = new Uri(news.WordPressBaseUrl.TrimEnd('/') + "/wp-json/vahac/v1/news/");
            client.Timeout = Timeout.InfiniteTimeSpan; // Each call sets its own timeout; item calls wait for an image download.
            client.MaxResponseContentBufferSize = 256 * 1024;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AgentGateway/0.0.0-dev");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
          .RemoveAllLoggers(); // Every request carries the Application Password in its Authorization header.
        return services;
    }
}
