using System.Net;
using Microsoft.EntityFrameworkCore;
using AgentGateway.TestInfrastructure.Http;
using AgentGateway.TestInfrastructure.News;
using AgentGateway.TestInfrastructure.Time;

namespace AgentGateway.Api.Tests;

/// <summary>The news watchdog: one Telegram alert per missing issue date, driven with a fake clock through the real outbox.</summary>
public sealed class NewsWatchdogTests
{
    private const string Issues = "/api/news/issues";
    private static readonly DateTimeOffset CheckTime = new(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Check_after_the_check_time_without_todays_issue_queues_one_alert_naming_the_date_and_the_last_issue()
    {
        //Arrange
        await using var factory = new GatewayApiFactory { Time = new MutableTimeProvider(CheckTime.AddSeconds(30)) };
        factory.TelegramHttp.ArmSuccess();
        await SubmitAsync(factory, new DateOnly(2026, 11, 15));
        await SubmitAsync(factory, new DateOnly(2027, 11, 16), "fixture"); // check-news.sh parks its fixture a year ahead.
        //Act
        var alerted = await factory.CheckWatchdogOnceAsync();
        await factory.ProcessOnceAsync();
        //Assert
        Assert.True(alerted);
        Assert.Equal("⚠️ Техдайджест за 16.11.2026 не надійшов: о 06:00 UTC випуску в шлюзі немає. Останній випуск: 15.11.2026. "
            + "Перевір запуск routine «Daily tech news digest» у claude.ai → Code → Routines. Докладніше: README → News watchdog.",
            Shown(Assert.Single(factory.TelegramHttp.Requests).Text));
        Assert.False(Assert.Single(factory.TelegramHttp.Requests).DisableNotification);
        await factory.WithDbAsync(async database => Assert.Equal("news-watchdog-2026-11-16",
            (await database.Deliveries.SingleAsync(TestContext.Current.CancellationToken)).IdempotencyKey));
    }

    [Fact]
    public async Task Check_with_todays_issue_in_any_status_sends_nothing()
    {
        //Arrange
        await using var factory = new GatewayApiFactory { Time = new MutableTimeProvider(CheckTime.AddHours(1)) };
        await SubmitAsync(factory, new DateOnly(2026, 11, 16)); // Still Pending: arrival is what counts, not publication.
        //Act
        var alerted = await factory.CheckWatchdogOnceAsync();
        //Assert
        Assert.False(alerted);
        await factory.WithDbAsync(async database => Assert.Empty(await database.Deliveries.ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Check_before_the_check_time_looks_at_the_previous_day()
    {
        //Arrange
        await using var factory = new GatewayApiFactory { Time = new MutableTimeProvider(CheckTime.AddMinutes(-1)) };
        await SubmitAsync(factory, new DateOnly(2026, 11, 15));
        //Act
        var alerted = await factory.CheckWatchdogOnceAsync();
        //Assert
        Assert.False(alerted); // Today's 06:00 has not come yet, and yesterday's issue exists.
        await factory.WithDbAsync(async database => Assert.Empty(await database.Deliveries.ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Check_repeated_and_after_a_restart_alerts_exactly_once()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), "agent-gateway-watchdog-" + Guid.NewGuid().ToString("N"));
        try
        {
            bool first, repeated, afterRestart;
            await using (var before = new GatewayApiFactory(path) { Time = new MutableTimeProvider(CheckTime.AddSeconds(30)) })
            {
                first = await before.CheckWatchdogOnceAsync();
                repeated = await before.CheckWatchdogOnceAsync();
            }
            //Act
            await using var restarted = new GatewayApiFactory(path) { Time = new MutableTimeProvider(CheckTime.AddHours(3)) };
            restarted.TelegramHttp.ArmSuccess();
            afterRestart = await restarted.CheckWatchdogOnceAsync();
            await restarted.ProcessOnceAsync();
            //Assert
            Assert.True(first);
            Assert.False(repeated);
            Assert.False(afterRestart);
            Assert.Single(restarted.TelegramHttp.Requests);
            await restarted.WithDbAsync(async database => Assert.Single(await database.Deliveries.ToListAsync(TestContext.Current.CancellationToken)));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    [Fact]
    public async Task Check_after_a_restart_past_the_check_time_catches_up()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), "agent-gateway-watchdog-" + Guid.NewGuid().ToString("N"));
        try
        {
            bool early;
            await using (var before = new GatewayApiFactory(path) { Time = new MutableTimeProvider(CheckTime.AddHours(-1)) })
            {
                await SubmitAsync(before, new DateOnly(2026, 11, 15));
                early = await before.CheckWatchdogOnceAsync();
            }
            //Act
            await using var restarted = new GatewayApiFactory(path) { Time = new MutableTimeProvider(CheckTime.AddHours(3.5)) };
            var caughtUp = await restarted.CheckWatchdogOnceAsync();
            //Assert
            Assert.False(early);
            Assert.True(caughtUp);
            await restarted.WithDbAsync(async database => Assert.Equal("news-watchdog-2026-11-16",
                (await database.Deliveries.SingleAsync(TestContext.Current.CancellationToken)).IdempotencyKey));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    [Fact]
    public async Task Check_without_any_earlier_issue_says_there_was_none_yet()
    {
        //Arrange
        await using var factory = new GatewayApiFactory { Time = new MutableTimeProvider(CheckTime.AddMinutes(1)) };
        factory.TelegramHttp.ArmSuccess();
        //Act
        await factory.CheckWatchdogOnceAsync();
        await factory.ProcessOnceAsync();
        //Assert
        Assert.Contains("Останній випуск: ще не було.", Shown(Assert.Single(factory.TelegramHttp.Requests).Text));
    }

    [Fact]
    public async Task Check_in_Europe_Kyiv_fires_at_the_local_wall_clock_time()
    {
        //Arrange
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 11, 16, 5, 59, 0, TimeSpan.Zero)); // 07:59 in Kyiv (UTC+2 in November).
        await using var factory = new GatewayApiFactory { Time = time };
        factory.Settings["News:WatchdogTime"] = "08:00";
        factory.Settings["News:WatchdogTimeZone"] = "Europe/Kyiv";
        factory.TelegramHttp.ArmSuccess();
        await SubmitAsync(factory, new DateOnly(2026, 11, 15));
        var beforeTime = await factory.CheckWatchdogOnceAsync();
        time.Advance(TimeSpan.FromMinutes(1));
        //Act
        var atTime = await factory.CheckWatchdogOnceAsync();
        await factory.ProcessOnceAsync();
        //Assert
        Assert.False(beforeTime);
        Assert.True(atTime);
        Assert.Contains("о 08:00 Europe/Kyiv випуску в шлюзі немає", Shown(Assert.Single(factory.TelegramHttp.Requests).Text));
    }

    [Theory]
    [InlineData("News:WatchdogEnabled")]
    [InlineData("News:Enabled")]
    public async Task Check_switched_off_sends_nothing(string setting)
    {
        //Arrange
        await using var factory = new GatewayApiFactory { Time = new MutableTimeProvider(CheckTime.AddMinutes(1)) };
        factory.Settings[setting] = "false";
        //Act
        var alerted = await factory.CheckWatchdogOnceAsync();
        //Assert
        Assert.False(alerted);
        await factory.WithDbAsync(async database => Assert.Empty(await database.Deliveries.ToListAsync(TestContext.Current.CancellationToken)));
    }

    private static async Task SubmitAsync(GatewayApiFactory factory, DateOnly date, string prefix = "story")
    {
        var response = await factory.SendAsync(HttpMethod.Post, Issues, NewsIssueDataFactory.Issue(date, count: 5, prefix: prefix));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    /// <summary>The alert goes out as Telegram HTML, where some characters travel as entities; this is the text the chat shows.</summary>
    private static string Shown(string html) => WebUtility.HtmlDecode(html);
}
