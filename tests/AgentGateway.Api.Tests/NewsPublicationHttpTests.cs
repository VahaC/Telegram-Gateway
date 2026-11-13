using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AgentGateway.Api.Contracts.News;
using AgentGateway.Core.Enums;
using AgentGateway.TestInfrastructure.Http;
using AgentGateway.TestInfrastructure.News;

namespace AgentGateway.Api.Tests;

public sealed class NewsPublicationHttpTests
{
    private const string Issues = "/api/news/issues";
    private const string StatusPath = "/api/news/issues/2026-11-14";
    private const string DayUrl = FakeWordPressHandler.BaseUrl + "/news/2026-11-14/";
    private static readonly DateOnly Date = new(2026, 11, 14);

    [Fact]
    public async Task Publish_sends_day_then_items_in_number_order_then_verifies_and_queues_one_notice()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ArmSuccess();
        var issue = NewsIssueDataFactory.Issue(Date);
        await factory.SendAsync(HttpMethod.Post, Issues, issue);
        //Act
        var worked = await factory.ProcessNewsOnceAsync();
        await factory.ProcessOnceAsync();
        var status = await GetStatusAsync(factory);
        //Assert
        Assert.True(worked);
        var calls = factory.WordPressHttp.Calls;
        Assert.Equal(["PUT days/2026-11-14", .. Enumerable.Range(1, 38).Select(number => $"PUT items/story-20261114-{number}"), "GET days/2026-11-14"],
            calls.Select(call => call.Call).ToList());
        Assert.All(calls, call => Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("news-publisher:" + GatewayApiFactory.WordPressAppPassword)), call.Authorization));
        using (var day = JsonDocument.Parse(calls[0].Body!))
        {
            var root = day.RootElement;
            Assert.False(root.TryGetProperty("items", out _));
            Assert.DoesNotContain("summary", calls[0].Body!);
            Assert.DoesNotContain("whyItMatters", calls[0].Body!);
            Assert.Equal(38, root.GetProperty("itemOrder").GetArrayLength());
            Assert.Equal("story-20261114-1", root.GetProperty("itemOrder")[0].GetProperty("eventKey").GetString());
            Assert.True(root.GetProperty("itemOrder")[0].GetProperty("isTop").GetBoolean());
            Assert.Equal("2026-11-14", root.GetProperty("date").GetString());
            Assert.Equal(2, root.GetProperty("releases").GetArrayLength());
            Assert.Equal(1, root.GetProperty("securityAlerts").GetArrayLength());
            Assert.Equal(48, root.GetProperty("research").GetProperty("windowHours").GetInt32());
            Assert.Equal(status.Id, root.GetProperty("gateway").GetProperty("issueId").GetGuid());
            Assert.Equal(1, root.GetProperty("gateway").GetProperty("revision").GetInt32());
            Assert.Matches("^\\d{4}-\\d\\d-\\d\\dT\\d\\d:\\d\\d:\\d\\dZ$", root.GetProperty("gateway").GetProperty("submittedAt").GetString());
        }
        using (var item = JsonDocument.Parse(calls[7].Body!))
        {
            var root = item.RootElement;
            Assert.Equal(7, root.GetProperty("number").GetInt32());
            Assert.Equal("2026-11-14", root.GetProperty("issueDate").GetString());
            Assert.Equal("Перший абзац новини 7.\n\nДругий абзац.", root.GetProperty("summary").GetString());
            Assert.Equal("https://news.example/story/20261114/7", root.GetProperty("source").GetProperty("url").GetString());
            Assert.Equal("https://images.example/story/7.jpg", root.GetProperty("image").GetProperty("url").GetString());
            Assert.False(root.TryGetProperty("eventOn", out _));
            Assert.Equal(1, root.GetProperty("gateway").GetProperty("revision").GetInt32());
            Assert.Contains("Перший абзац", calls[7].Body!); // UTF-8 text, not \u escapes.
        }
        Assert.Equal(NewsPublicationStatus.Published, status.Status);
        Assert.Equal(DayUrl, status.DayUrl);
        Assert.Equal(38, status.PublishedItemCount);
        Assert.All(status.Items, entry => Assert.Equal("attached", entry.Image));
        Assert.Equal(FakeWordPressHandler.BaseUrl + "/news/2026-11-14/story-20261114-7/", status.Items[6].Url);
        var notice = Assert.Single(factory.TelegramHttp.Requests);
        Assert.Equal("📰 Техдайджест 14.11.2026 — 38 новин: " + DayUrl, Shown(notice.Text));
        Assert.NotNull(status.Notice);
        await factory.WithDbAsync(async database =>
        {
            var stored = await database.NewsIssues.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(NewsPublicationStatus.Published, stored.Status);
            Assert.NotNull(stored.PublishedUtc);
            Assert.Equal(1, stored.NoticeRevision);
            Assert.Equal("news-published-2026-11-14-r1", (await database.Deliveries.SingleAsync(row => row.Id == stored.NoticeDeliveryId, TestContext.Current.CancellationToken)).IdempotencyKey);
        });
        Assert.False(await factory.ProcessNewsOnceAsync());
        Assert.Equal(40, factory.WordPressHttp.Requests.Count);
    }

    [Fact]
    public async Task Publish_retries_item_after_500_and_after_lost_response_with_the_same_revision()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.WordPressHttp.Override = call => call switch
        {
            { Path: "items/story-20261114-7", Attempt: 1 } => FakeWordPressHandler.Json(HttpStatusCode.InternalServerError, "{}"),
            { Path: "items/story-20261114-9", Attempt: 1 } => throw new TaskCanceledException("simulated timeout"),
            _ => null
        };
        await factory.SendAsync(HttpMethod.Post, Issues, NewsIssueDataFactory.Issue(Date));
        //Act
        await factory.ProcessNewsOnceAsync();
        var status = await GetStatusAsync(factory);
        //Assert
        Assert.Equal(NewsPublicationStatus.Published, status.Status);
        var seven = factory.WordPressHttp.Calls.Where(call => call.Path == "items/story-20261114-7").ToList();
        var nine = factory.WordPressHttp.Calls.Where(call => call.Path == "items/story-20261114-9").ToList();
        Assert.Equal(2, seven.Count);
        Assert.Equal(2, nine.Count);
        Assert.Equal(nine[0].Body, nine[1].Body); // The retry repeats the same revision, which WordPress treats as an upsert.
        Assert.Equal(2, status.Items[6].Attempts);
        Assert.Equal(2, status.Items[8].Attempts);
        Assert.Null(status.Items[8].ErrorCode);
    }

    [Fact]
    public async Task Publish_401_fails_with_wp_401_without_retry_or_notice()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.WordPressHttp.Override = _ => FakeWordPressHandler.Json(HttpStatusCode.Unauthorized,
            "{\"code\":\"rest_not_logged_in\",\"message\":\"private detail\"}");
        await factory.SendAsync(HttpMethod.Post, Issues, NewsIssueDataFactory.Issue(Date));
        //Act
        await factory.ProcessNewsOnceAsync();
        var response = await factory.SendAsync(HttpMethod.Get, StatusPath);
        var status = (await response.Content.ReadFromJsonAsync<NewsIssueStatusResponse>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken))!;
        //Assert
        Assert.Equal(NewsPublicationStatus.Failed, status.Status);
        Assert.Equal("wp_401", status.ErrorCode);
        Assert.Single(factory.WordPressHttp.Requests);
        Assert.DoesNotContain("private detail", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Null(status.Notice);
        await factory.WithDbAsync(async database => Assert.Empty(await database.Deliveries.ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Publish_item_failing_for_good_is_partial_and_republish_sends_only_that_item_without_second_notice()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ArmSuccess();
        factory.WordPressHttp.Override = call => call.Path == "items/story-20261114-12"
            ? FakeWordPressHandler.Json(HttpStatusCode.BadGateway, "{}") : null;
        await factory.SendAsync(HttpMethod.Post, Issues, NewsIssueDataFactory.Issue(Date));
        await factory.ProcessNewsOnceAsync();
        await factory.ProcessOnceAsync();
        var partial = await GetStatusAsync(factory);
        var callsBefore = factory.WordPressHttp.Requests.Count;
        factory.WordPressHttp.Override = null;
        //Act
        var republish = await factory.SendAsync(HttpMethod.Post, StatusPath + "/republish");
        await factory.ProcessNewsOnceAsync();
        await factory.ProcessOnceAsync();
        var published = await GetStatusAsync(factory);
        var noOp = await factory.SendAsync(HttpMethod.Post, StatusPath + "/republish");
        //Assert
        Assert.Equal(NewsPublicationStatus.PartiallyPublished, partial.Status);
        Assert.Equal("wp_5xx", partial.ErrorCode);
        Assert.Equal(NewsItemStatus.Failed, partial.Items[11].Status);
        Assert.Equal(6, partial.Items[11].Attempts); // News:MaxAttempts default.
        Assert.Equal(37, partial.PublishedItemCount);
        Assert.Equal("📰 Техдайджест 14.11.2026 — 37 з 38 новин: " + DayUrl, Shown(Assert.Single(factory.TelegramHttp.Requests).Text));
        Assert.Equal(HttpStatusCode.Accepted, republish.StatusCode);
        Assert.Equal(["PUT items/story-20261114-12", "GET days/2026-11-14"],
            factory.WordPressHttp.Calls.Skip(callsBefore).Select(call => call.Call).ToList());
        Assert.Equal(NewsPublicationStatus.Published, published.Status);
        Assert.Single(factory.TelegramHttp.Requests); // Once per revision, never twice.
        Assert.Equal(HttpStatusCode.OK, noOp.StatusCode);
        Assert.False(await factory.ProcessNewsOnceAsync());
    }

    [Fact]
    public async Task Publish_image_failure_is_recorded_but_the_issue_is_published()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        var issue = NewsIssueDataFactory.Issue(Date).WithItems(item => item.Number switch
        {
            3 => item with { Image = new("https://images.example/broken.jpg", "Broken") },
            4 => item with { Image = null },
            _ => item
        });
        await factory.SendAsync(HttpMethod.Post, Issues, issue);
        //Act
        await factory.ProcessNewsOnceAsync();
        var status = await GetStatusAsync(factory);
        //Assert
        Assert.Equal(NewsPublicationStatus.Published, status.Status);
        Assert.Equal("failed", status.Items[2].Image);
        Assert.Equal("http_404", status.Items[2].ImageError);
        Assert.Equal(NewsItemStatus.Published, status.Items[2].Status);
        Assert.Equal("none", status.Items[3].Image);
        await factory.WithDbAsync(async database => Assert.Equal("failed",
            (await database.NewsItems.SingleAsync(item => item.Number == 3, TestContext.Current.CancellationToken)).ImageResult));
    }

    [Fact]
    public async Task Publish_restart_after_item_20_resumes_at_item_21()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), "agent-gateway-news-restart-" + Guid.NewGuid().ToString("N"));
        var site = new FakeWordPressHandler();
        try
        {
            await using (var first = new GatewayApiFactory(path) { WordPressHttp = site })
            {
                using var stop = new CancellationTokenSource();
                site.Override = call =>
                {
                    if (call.Path != "items/story-20261114-21") return null;
                    stop.Cancel(); // The host stops while item 21 is in flight.
                    throw new OperationCanceledException(stop.Token);
                };
                await first.SendAsync(HttpMethod.Post, Issues, NewsIssueDataFactory.Issue(Date));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.ProcessNewsWithTokenAsync(stop.Token));
            }
            site.Override = null;
            var before = site.Requests.Count;
            //Act
            await using var restarted = new GatewayApiFactory(path) { WordPressHttp = site };
            await restarted.ProcessNewsOnceAsync();
            var status = await GetStatusAsync(restarted);
            //Assert
            Assert.Equal([.. Enumerable.Range(21, 18).Select(number => $"PUT items/story-20261114-{number}"), "GET days/2026-11-14"],
                site.Calls.Skip(before).Select(call => call.Call).ToList());
            Assert.Equal(NewsPublicationStatus.Published, status.Status);
            Assert.Equal(2, status.Attempts);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    [Fact]
    public async Task Publish_verify_reporting_missing_item_is_not_published()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ArmSuccess();
        factory.WordPressHttp.Override = call => call.Call == "GET days/2026-11-14"
            ? FakeWordPressHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                postId = 1, url = DayUrl, status = "private", revision = 1, expectedItems = 38, publishedItems = 37, missing = new[] { "story-20261114-5" }
            }))
            : null;
        await factory.SendAsync(HttpMethod.Post, Issues, NewsIssueDataFactory.Issue(Date));
        //Act
        await factory.ProcessNewsOnceAsync();
        await factory.ProcessOnceAsync();
        var status = await GetStatusAsync(factory);
        //Assert
        Assert.NotEqual(NewsPublicationStatus.Published, status.Status);
        Assert.Equal(NewsPublicationStatus.PartiallyPublished, status.Status);
        Assert.Equal(NewsItemStatus.Failed, status.Items[4].Status);
        Assert.Equal("wp_missing", status.Items[4].ErrorCode);
        Assert.Contains("37 з 38", Assert.Single(factory.TelegramHttp.Requests).Text);
    }

    [Fact]
    public async Task Publish_verify_with_unexpected_counts_is_verify_mismatch()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.WordPressHttp.Override = call => call.Call == "GET days/2026-11-14"
            ? FakeWordPressHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                postId = 1, url = DayUrl, status = "private", revision = 1, expectedItems = 30, publishedItems = 30, missing = Array.Empty<string>()
            }))
            : null;
        await factory.SendAsync(HttpMethod.Post, Issues, NewsIssueDataFactory.Issue(Date));
        //Act
        await factory.ProcessNewsOnceAsync();
        var status = await GetStatusAsync(factory);
        //Assert
        Assert.Equal(NewsPublicationStatus.PartiallyPublished, status.Status);
        Assert.Equal("verify_mismatch", status.ErrorCode);
    }

    [Fact]
    public async Task Replace_sends_revision_2_for_every_item_deletes_the_dropped_one_and_notifies_once_per_revision()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ArmSuccess();
        var issue = NewsIssueDataFactory.Issue(Date);
        await factory.SendAsync(HttpMethod.Post, Issues, issue);
        await factory.ProcessNewsOnceAsync();
        await factory.ProcessOnceAsync();
        var before = factory.WordPressHttp.Requests.Count;
        var replacement = issue with
        {
            Items = issue.Items.Where(item => item.Number != 38).Select(item => item.Number == 1 ? item with { Title = "Виправлено" } : item).ToList()
        };
        //Act
        var response = await factory.SendAsync(HttpMethod.Put, StatusPath, replacement);
        await factory.ProcessNewsOnceAsync();
        await factory.ProcessOnceAsync();
        var status = await GetStatusAsync(factory);
        var again = await factory.ProcessNewsOnceAsync();
        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var calls = factory.WordPressHttp.Calls.Skip(before).ToList();
        Assert.Equal(["PUT days/2026-11-14", .. Enumerable.Range(1, 37).Select(number => $"PUT items/story-20261114-{number}"),
            "DELETE items/story-20261114-38", "GET days/2026-11-14"], calls.Select(call => call.Call).ToList());
        Assert.All(calls.Where(call => call.Method == "PUT"), call =>
            Assert.Equal(2, JsonDocument.Parse(call.Body!).RootElement.GetProperty("gateway").GetProperty("revision").GetInt32()));
        Assert.Equal(37, JsonDocument.Parse(calls[0].Body!).RootElement.GetProperty("itemOrder").GetArrayLength());
        Assert.Equal(2, JsonDocument.Parse(calls.Single(call => call.Method == "DELETE").Body!).RootElement
            .GetProperty("gateway").GetProperty("revision").GetInt32());
        Assert.Equal(NewsPublicationStatus.Published, status.Status);
        Assert.Equal(2, status.Revision);
        Assert.Equal(37, status.ItemCount);
        Assert.Empty(status.Removals);
        Assert.All(status.Items, item => Assert.Equal(2, item.PublishedRevision));
        Assert.Equal("unchanged", status.Items[1].Image); // The same image is not downloaded again on a later revision.
        Assert.Equal(["📰 Техдайджест 14.11.2026 — 38 новин: " + DayUrl, "📰 Техдайджест 14.11.2026 — 37 новин: " + DayUrl],
            factory.TelegramHttp.Requests.Select(request => Shown(request.Text)).ToList());
        Assert.False(again);
        await factory.WithDbAsync(async database =>
        {
            Assert.Equal(2, await database.Deliveries.CountAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await database.NewsRemovals.ToListAsync(TestContext.Current.CancellationToken));
            Assert.False(await database.NewsItems.AnyAsync(item => item.EventKey == "story-20261114-38", TestContext.Current.CancellationToken));
        });
    }

    [Fact]
    public async Task Fixture_cleanup_trashes_item_posts_frees_the_registry_and_removes_the_issue()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        var fixture = NewsIssueDataFactory.Issue(Date, count: 36, prefix: "fixture") with { Title = "FIXTURE — not real news" };
        var real = NewsIssueDataFactory.Issue(Date.AddDays(1), prefix: "real");
        await factory.SendAsync(HttpMethod.Post, Issues, fixture);
        await factory.SendAsync(HttpMethod.Post, Issues, real);
        await factory.ProcessNewsOnceAsync();
        await factory.ProcessNewsOnceAsync();
        var before = factory.WordPressHttp.Requests.Count;
        //Act
        var refused = await factory.SendAsync(HttpMethod.Delete, Issues + "/2026-11-15");
        var accepted = await factory.SendAsync(HttpMethod.Delete, StatusPath);
        var resubmitBeforeWorker = await factory.SendAsync(HttpMethod.Post, Issues, fixture with { Date = Date.AddDays(2) });
        await factory.ProcessNewsOnceAsync();
        var gone = await factory.SendAsync(HttpMethod.Get, StatusPath);
        //Assert
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, resubmitBeforeWorker.StatusCode); // The registry was freed at once.
        var deletes = factory.WordPressHttp.Calls.Skip(before).Where(call => call.Method == "DELETE").Select(call => call.Path).ToList();
        Assert.Equal(36, deletes.Count);
        Assert.All(deletes, path => Assert.StartsWith("items/fixture-20261114-", path));
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        await factory.WithDbAsync(async database =>
        {
            Assert.Equal(2, await database.NewsIssues.CountAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await database.NewsRemovals.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Equal(38, await database.NewsItems.CountAsync(item => item.EventKey.StartsWith("real-"), TestContext.Current.CancellationToken));
        });
    }

    [Fact]
    public async Task Fixture_cleanup_that_failed_retries_its_removals_when_requested_again()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        await factory.SendAsync(HttpMethod.Post, Issues, NewsIssueDataFactory.Issue(Date, count: 36, prefix: "fixture") with { Title = "FIXTURE — not real news" });
        await factory.ProcessNewsOnceAsync();
        factory.WordPressHttp.Override = call => call.Method == "DELETE" ? FakeWordPressHandler.Json(HttpStatusCode.BadGateway, "{}") : null;
        await factory.SendAsync(HttpMethod.Delete, StatusPath);
        await factory.ProcessNewsOnceAsync();
        var failed = await GetStatusAsync(factory);
        factory.WordPressHttp.Override = null;
        //Act
        var again = await factory.SendAsync(HttpMethod.Delete, StatusPath);
        await factory.ProcessNewsOnceAsync();
        var gone = await factory.SendAsync(HttpMethod.Get, StatusPath);
        //Assert
        Assert.Equal(NewsPublicationStatus.Failed, failed.Status);
        Assert.Equal("wp_5xx", failed.ErrorCode);
        Assert.Equal(36, failed.Removals.Count);
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Publish_without_notice_setting_sends_nothing_to_telegram()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.Settings["News:NotifyTelegramOnPublish"] = "false";
        await factory.SendAsync(HttpMethod.Post, Issues, NewsIssueDataFactory.Issue(Date));
        //Act
        await factory.ProcessNewsOnceAsync();
        var status = await GetStatusAsync(factory);
        //Assert
        Assert.Equal(NewsPublicationStatus.Published, status.Status);
        Assert.Null(status.Notice);
        await factory.WithDbAsync(async database => Assert.Empty(await database.Deliveries.ToListAsync(TestContext.Current.CancellationToken)));
    }

    /// <summary>The notice goes out as Telegram HTML, where the emoji travels as a numeric entity; this is the text the chat shows.</summary>
    private static string Shown(string html) => WebUtility.HtmlDecode(html);

    private static async Task<NewsIssueStatusResponse> GetStatusAsync(GatewayApiFactory factory)
    {
        var response = await factory.SendAsync(HttpMethod.Get, StatusPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NewsIssueStatusResponse>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken))!;
    }
}
