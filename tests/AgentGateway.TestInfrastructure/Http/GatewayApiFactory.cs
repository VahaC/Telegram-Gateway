using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using AgentGateway.Api;
using AgentGateway.Api.Services.Delivery;
using AgentGateway.Api.Services.News;
using AgentGateway.Infrastructure.Persistence;

namespace AgentGateway.TestInfrastructure.Http;

public sealed class GatewayApiFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "test-api-key-never-for-production-0123456789";
    public string DataPath { get; }
    public const string WordPressAppPassword = "test-app-password-not-real-0000";
    public FakeTelegramHandler TelegramHttp { get; } = new();
    /// <summary>Settable so a restarted factory can talk to the same fake site.</summary>
    public FakeWordPressHandler WordPressHttp { get; set; } = new();
    public Dictionary<string, string?> Settings { get; } = [];
    public TimeProvider? Time { get; set; }
    /// <summary>Receives the application's log entries in addition to its own console logger.</summary>
    public ILoggerProvider? LoggerProvider { get; set; }
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private HttpClient? client;
    private readonly bool ownsData;

    public GatewayApiFactory(string? dataPath = null)
    {
        ownsData = dataPath is null;
        DataPath = dataPath ?? Path.Combine(Path.GetTempPath(), $"agent-gateway-tests-{Guid.NewGuid():N}");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TELEGRAM_BOT_TOKEN"] = "123456:test-token-not-real",
            ["TELEGRAM_CHAT_ID"] = "123456789",
            ["API_KEY"] = ApiKey,
            ["Gateway:DataPath"] = DataPath,
            ["Gateway:MessageIntervalMilliseconds"] = "0",
            ["Gateway:RetryBaseMilliseconds"] = "0",
            ["Gateway:MaxAttempts"] = "3",
            ["Gateway:EnableMcp"] = "true",
            ["Gateway:RequestLimitPerMinute"] = "1000",
            ["Gateway:AllowedHosts:0"] = "localhost",
            ["News:Enabled"] = "true",
            ["News:WordPressBaseUrl"] = FakeWordPressHandler.BaseUrl,
            ["News:WordPressUser"] = "news-publisher",
            ["WORDPRESS_APP_PASSWORD"] = WordPressAppPassword,
            ["News:CallIntervalMilliseconds"] = "0",
            ["News:RetryBaseMilliseconds"] = "0"
        }).AddInMemoryCollection(Settings));
        if (LoggerProvider is not null) builder.ConfigureLogging(logging => logging.AddProvider(LoggerProvider));
        builder.ConfigureServices(services =>
        {
            if (Time is not null) services.AddSingleton(Time);
            var worker = services.Single(service => service.ServiceType == typeof(IHostedService) && service.ImplementationType == typeof(DeliveryWorker));
            services.Remove(worker); // Background delivery is driven deterministically by ProcessOnceAsync.
            services.Remove(services.Single(service => service.ServiceType == typeof(IHostedService)
                && service.ImplementationType == typeof(NewsPublicationWorker))); // Publication is driven by ProcessNewsOnceAsync.
            services.Configure<HttpClientFactoryOptions>("WordPress", options => options.HttpMessageHandlerBuilderActions
                .Add(handler => handler.PrimaryHandler = WordPressHttp));
            services.Configure<HttpClientFactoryOptions>("Telegram", options => options.HttpMessageHandlerBuilderActions
                .Add(handler => handler.PrimaryHandler = TelegramHttp));
        });
    }

    public HttpClient Client => client ??= CreateClient();

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, bool authenticated = true)
    {
        using var request = new HttpRequestMessage(method, path);
        if (authenticated) request.Headers.Add("X-Api-Key", ApiKey);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        return await Client.SendAsync(request);
    }

    public async Task<bool> ProcessOnceAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DeliveryProcessor>().ProcessOnceAsync(CancellationToken.None);
    }

    public Task<bool> ProcessNewsOnceAsync() => ProcessNewsWithTokenAsync(CancellationToken.None);

    /// <summary>Lets a test stop the host token in the middle of a run, as a shutdown would.</summary>
    public async Task<bool> ProcessNewsWithTokenAsync(CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NewsPublicationProcessor>().ProcessOnceAsync(cancellationToken);
    }

    public async Task WithDbAsync(Func<ApplicationDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        // Connections are released before deleting only this factory's random, verified temp directory.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (ownsData && Directory.Exists(DataPath)) Directory.Delete(DataPath, true);
    }
}
