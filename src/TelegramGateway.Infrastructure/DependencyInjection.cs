using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TelegramGateway.Core.Abstractions;
using TelegramGateway.Core.Options;
using TelegramGateway.Infrastructure.Formatting;
using TelegramGateway.Infrastructure.Persistence;
using TelegramGateway.Infrastructure.Telegram;

namespace TelegramGateway.Infrastructure;

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
            options.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly("TelegramGateway.Api"));
            options.AddInterceptors(provider.GetRequiredService<SqlitePragmaInterceptor>());
        });
        services.AddSingleton<IMessageFormatter, TelegramMessageFormatter>();
        services.AddSingleton<ITelegramClient, TelegramClient>();
        services.AddHttpClient(TelegramClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.telegram.org/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.MaxResponseContentBufferSize = 64 * 1024;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TelegramGateway/0.0.0-dev");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
          .RemoveAllLoggers(); // The Telegram token is in the URL; default HttpClient logging would disclose it.
        return services;
    }
}
