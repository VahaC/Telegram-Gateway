using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using AgentGateway.Api.Contracts;
using AgentGateway.Api.Contracts.News;
using AgentGateway.Api.Services.News;
using AgentGateway.TestInfrastructure.Http;
using AgentGateway.TestInfrastructure.Logging;
using AgentGateway.TestInfrastructure.News;
using AgentGateway.TestInfrastructure.Time;

namespace AgentGateway.Api.Tests;

/// <summary>The feed collector (ADR 0004): catalog sync, polling against fake publishers, and GET /api/news/feed over real HTTP.</summary>
public sealed class NewsFeedHttpTests
{
    private const string FeedPath = "/api/news/feed";
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Feed_after_sync_and_poll_lists_entries_newest_first_with_their_feed()
    {
        //Arrange
        var time = new MutableTimeProvider(Now);
        await using var factory = Create(time, NewsFeedFixtures.Feed("alpha-news", "https://alpha.example/feed", "hardware", "smarthome"),
            NewsFeedFixtures.Feed("beta-blog", "https://beta.example/atom.xml", "dotnet"));
        var longText = "<p>ESP32-P4 <b>board</b> &amp; display.</p>" + string.Concat(Enumerable.Repeat("<p>More detail about the board.</p>", 30));
        factory.FeedHttp.ServeXml("https://alpha.example/feed", NewsFeedFixtures.Rss(
            new FeedItemFixture("Board <em>launch</em>", "https://alpha.example/board?utm_source=rss", Now.AddHours(-2), longText, "https://alpha.example/board.jpg"),
            new FeedItemFixture("Older story", "https://alpha.example/older", Now.AddHours(-30), "Plain text.", "http://alpha.example/insecure.jpg"),
            new FeedItemFixture("Undated story", "https://alpha.example/undated", null)));
        factory.FeedHttp.ServeXml("https://beta.example/atom.xml", NewsFeedFixtures.Atom(
            new FeedItemFixture(".NET 10.0.1 servicing", "https://beta.example/dotnet-10-0-1", Now.AddHours(-1), "<p>Fixes.</p>", "https://beta.example/cover.png")));
        await factory.SyncFeedCatalogAsync();
        //Act
        var summary = await factory.PollFeedsOnceAsync();
        var response = await factory.SendAsync(HttpMethod.Get, FeedPath);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var feed = await ReadAsync<NewsFeedResponse>(response);
        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new NewsFeedPollSummary(2, 2, 0, 0, 4, 0), summary);
        Assert.Equal(["Undated story", ".NET 10.0.1 servicing", "Board launch", "Older story"], feed.Entries.Select(entry => entry.Title).ToList());
        Assert.Equal(4, feed.Total);
        Assert.Null(feed.NextCursor);
        var undated = feed.Entries[0];
        Assert.Null(undated.PublishedUtc);
        Assert.Equal(Now.UtcDateTime, undated.FirstSeenUtc);
        var board = feed.Entries[2];
        Assert.Equal("alpha-news", board.FeedId);
        Assert.Equal("Feed alpha-news", board.FeedName);
        Assert.Equal(["hardware", "smarthome"], board.FeedCategories);
        Assert.Equal("news", board.Kind);
        Assert.Equal("https://alpha.example/board?utm_source=rss", board.Url);
        Assert.Equal(Now.AddHours(-2).UtcDateTime, board.PublishedUtc);
        Assert.StartsWith("ESP32-P4 board & display. More detail", board.Summary);
        Assert.True(board.Summary.Length <= NewsFeedService.ListSummaryLength);
        Assert.EndsWith("…", board.Summary);
        Assert.Equal("https://alpha.example/board.jpg", board.ImageUrl);
        Assert.Null(feed.Entries[3].ImageUrl); // http pictures are refused.
        Assert.Equal("https://beta.example/cover.png", feed.Entries[1].ImageUrl);
        Assert.Equal(new NewsFeedHealthResponse(2, 0, Now.UtcDateTime), feed.Health);
        Assert.Contains("\"publishedUtc\":\"2026-11-15T10:00:00.0000000Z\"", raw);
        await factory.WithDbAsync(async database =>
        {
            var stored = await database.NewsFeedEntries.SingleAsync(entry => entry.Title == "Board launch", TestContext.Current.CancellationToken);
            Assert.Equal("https://alpha.example/board", stored.CanonicalUrl);
            Assert.True(stored.Summary.Length > NewsFeedService.ListSummaryLength);
            Assert.DoesNotContain("<", stored.Summary);
            Assert.Equal(stored.PublishedUtc, stored.SortUtc);
            var feeds = await database.NewsFeeds.ToListAsync(TestContext.Current.CancellationToken);
            Assert.All(feeds, row => Assert.Equal(Now.UtcDateTime, row.LastSuccessUtc));
        });
    }

    [Fact]
    public async Task Poll_names_the_feed_reader_sends_a_conditional_get_and_a_304_adds_nothing()
    {
        //Arrange
        var time = new MutableTimeProvider(Now);
        await using var factory = Create(time, NewsFeedFixtures.Feed("alpha-news", "https://alpha.example/feed"));
        factory.FeedHttp.ServeXml("https://alpha.example/feed",
            NewsFeedFixtures.Rss(new FeedItemFixture("Story", "https://alpha.example/story", Now.AddHours(-1))), eTag: "\"v1\"");
        await factory.SyncFeedCatalogAsync();
        var first = await factory.PollFeedsOnceAsync();
        time.Advance(TimeSpan.FromHours(1));
        //Act
        var second = await factory.PollFeedsOnceAsync();
        //Assert
        var requests = factory.FeedHttp.Requests.ToList();
        Assert.Equal(2, requests.Count);
        Assert.All(requests, request => Assert.Equal("AgentGateway-NewsFeed/1.0 (personal feed reader)", request.UserAgent));
        Assert.Null(requests[0].IfNoneMatch);
        Assert.Equal("\"v1\"", requests[1].IfNoneMatch);
        Assert.Equal(1, first.NewEntries);
        Assert.Equal(new NewsFeedPollSummary(1, 1, 1, 0, 0, 0), second);
        await factory.WithDbAsync(async database =>
        {
            Assert.Single(await database.NewsFeedEntries.ToListAsync(TestContext.Current.CancellationToken));
            var feed = await database.NewsFeeds.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal("\"v1\"", feed.ETag);
            Assert.Equal(Now.AddHours(1).UtcDateTime, feed.LastSuccessUtc);
            Assert.Equal(0, feed.ConsecutiveFailures);
        });
    }

    [Fact]
    public async Task Poll_records_refusals_and_broken_feeds_once_each_and_keeps_the_good_feed()
    {
        //Arrange
        var time = new MutableTimeProvider(Now);
        var log = new CapturingLoggerProvider();
        await using var factory = Create(time,
            NewsFeedFixtures.Feed("forbidden", "https://forbidden.example/feed"), NewsFeedFixtures.Feed("payment", "https://payment.example/feed"),
            NewsFeedFixtures.Feed("limited", "https://limited.example/feed"), NewsFeedFixtures.Feed("html-page", "https://html.example/feed"),
            NewsFeedFixtures.Feed("with-dtd", "https://dtd.example/feed"), NewsFeedFixtures.Feed("huge", "https://huge.example/feed"),
            NewsFeedFixtures.Feed("good", "https://good.example/feed"));
        factory.LoggerProvider = log;
        factory.FeedHttp.ServeStatus("https://forbidden.example/feed", HttpStatusCode.Forbidden);
        factory.FeedHttp.ServeStatus("https://payment.example/feed", HttpStatusCode.PaymentRequired);
        factory.FeedHttp.ServeStatus("https://limited.example/feed", HttpStatusCode.TooManyRequests);
        factory.FeedHttp.ServeXml("https://html.example/feed", "<html><body>Just a moment...</body></html>");
        factory.FeedHttp.ServeXml("https://dtd.example/feed", "<?xml version=\"1.0\"?><!DOCTYPE rss [<!ENTITY x \"boom\">]><rss><channel/></rss>");
        factory.FeedHttp.ServeXml("https://huge.example/feed", "<rss><channel>" + new string(' ', 16 * 1024 * 1024) + "</channel></rss>");
        factory.FeedHttp.ServeXml("https://good.example/feed", NewsFeedFixtures.Rss(new FeedItemFixture("Good story", "https://good.example/story", Now.AddHours(-1))));
        await factory.SyncFeedCatalogAsync();
        //Act
        var summary = await factory.PollFeedsOnceAsync();
        await factory.PollFeedsOnceAsync();
        var feed = await ReadAsync<NewsFeedResponse>(await factory.SendAsync(HttpMethod.Get, FeedPath));
        //Assert
        Assert.Equal(new NewsFeedPollSummary(7, 1, 0, 6, 1, 0), summary);
        Assert.Equal(14, factory.FeedHttp.Requests.Count); // One request per feed and poll: nothing is retried.
        Assert.Equal("Good story", Assert.Single(feed.Entries).Title);
        Assert.Equal(new NewsFeedHealthResponse(7, 6, Now.UtcDateTime), feed.Health);
        await factory.WithDbAsync(async database =>
        {
            var errors = await database.NewsFeeds.ToDictionaryAsync(row => row.CatalogId, row => (row.LastError, row.ConsecutiveFailures),
                TestContext.Current.CancellationToken);
            Assert.Equal(("http_403", 2), errors["forbidden"]);
            Assert.Equal(("http_402", 2), errors["payment"]);
            Assert.Equal(("http_429", 2), errors["limited"]);
            Assert.Equal(("not_a_feed (root <html>)", 2), errors["html-page"]);
            Assert.StartsWith("xml_invalid", errors["with-dtd"].LastError);
            Assert.Equal(("too_large", 2), errors["huge"]);
            Assert.Equal((null, 0), errors["good"]);
        });
        Assert.Contains(log.Messages, message => message.StartsWith("News feeds polled: 1 of 7 ok (0 unchanged), 6 failed, 1 new entries", StringComparison.Ordinal)
            && message.Contains("forbidden http_403", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Poll_keeps_one_request_per_host_in_flight_and_leaves_a_host_alone_after_its_429()
    {
        //Arrange
        var time = new MutableTimeProvider(Now);
        var github = Enumerable.Range(1, 6).Select(number => NewsFeedFixtures.Feed($"github-{number}", $"https://github.com/owner/project-{number}/releases.atom"));
        var limited = Enumerable.Range(1, 3).Select(number => NewsFeedFixtures.Feed($"limited-{number}", $"https://limited.example/feed-{number}"));
        var others = Enumerable.Range(1, 3).Select(number => NewsFeedFixtures.Feed($"other-{number}", $"https://other-{number}.example/feed"));
        await using var factory = Create(time, [.. github, .. limited, .. others]);
        factory.FeedHttp.Delay = TimeSpan.FromMilliseconds(40);
        foreach (var number in Enumerable.Range(1, 6))
            factory.FeedHttp.ServeXml($"https://github.com/owner/project-{number}/releases.atom",
                NewsFeedFixtures.Atom(new FeedItemFixture($"project-{number} 1.0", $"https://github.com/owner/project-{number}/releases/tag/v1.0", Now.AddHours(-1))));
        foreach (var number in Enumerable.Range(1, 3))
            factory.FeedHttp.ServeXml($"https://other-{number}.example/feed", NewsFeedFixtures.Rss(new FeedItemFixture($"Other {number}", $"https://other-{number}.example/a", Now.AddHours(-1))));
        factory.FeedHttp.ServeStatus("https://limited.example/feed-1", HttpStatusCode.TooManyRequests);
        await factory.SyncFeedCatalogAsync();
        //Act
        var summary = await factory.PollFeedsOnceAsync();
        //Assert
        Assert.Equal(1, factory.FeedHttp.MaxInFlightPerHost);
        Assert.Equal(6, factory.FeedHttp.Requests.Count(request => request.Url.StartsWith("https://github.com/", StringComparison.Ordinal)));
        Assert.Equal(["https://limited.example/feed-1"],
            factory.FeedHttp.Requests.Where(request => request.Url.Contains("limited", StringComparison.Ordinal)).Select(request => request.Url).ToList());
        Assert.Equal(new NewsFeedPollSummary(12, 9, 0, 3, 9, 0), summary);
        await factory.WithDbAsync(async database =>
        {
            var errors = await database.NewsFeeds.Where(row => row.CatalogId.StartsWith("limited-")).OrderBy(row => row.CatalogId)
                .Select(row => row.LastError).ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(["http_429", NewsFeedPoller.SkippedAfterRateLimit, NewsFeedPoller.SkippedAfterRateLimit], errors);
        });
    }

    [Fact]
    public async Task Sync_adds_updates_and_disables_feeds_and_keeps_the_entries_of_a_removed_feed()
    {
        //Arrange
        var time = new MutableTimeProvider(Now);
        await using var factory = Create(time, NewsFeedFixtures.Feed("alpha", "https://alpha.example/feed"), NewsFeedFixtures.Feed("beta", "https://beta.example/feed"));
        factory.FeedHttp.ServeXml("https://alpha.example/feed", NewsFeedFixtures.Rss(new FeedItemFixture("Alpha story", "https://alpha.example/a", Now.AddHours(-1))), eTag: "\"a1\"");
        factory.FeedHttp.ServeXml("https://beta.example/feed", NewsFeedFixtures.Rss(new FeedItemFixture("Beta story", "https://beta.example/b", Now.AddHours(-1))));
        factory.FeedHttp.ServeXml("https://alpha.example/new-feed", NewsFeedFixtures.Rss());
        factory.FeedHttp.ServeXml("https://gamma.example/feed", NewsFeedFixtures.Rss());
        await factory.SyncFeedCatalogAsync();
        await factory.PollFeedsOnceAsync();
        NewsFeedFixtures.WriteCatalog(factory.DataPath, NewsFeedFixtures.Feed("alpha", "https://alpha.example/new-feed", "dotnet") with { Name = "Alpha renamed" },
            NewsFeedFixtures.Feed("gamma", "https://gamma.example/feed", "ai"));
        var before = factory.FeedHttp.Requests.Count;
        //Act
        var applied = await factory.SyncFeedCatalogAsync();
        await factory.PollFeedsOnceAsync();
        var feed = await ReadAsync<NewsFeedResponse>(await factory.SendAsync(HttpMethod.Get, FeedPath));
        //Assert
        Assert.True(applied);
        Assert.Equal(["https://alpha.example/new-feed", "https://gamma.example/feed"],
            factory.FeedHttp.Requests.Skip(before).Select(request => request.Url).Order().ToList());
        Assert.Null(factory.FeedHttp.Requests.Skip(before).First(request => request.Url.Contains("alpha", StringComparison.Ordinal)).IfNoneMatch);
        Assert.Equal(["Alpha story", "Beta story"], feed.Entries.Select(entry => entry.Title).Order().ToList());
        Assert.Equal(2, feed.Health.FeedsEnabled);
        await factory.WithDbAsync(async database =>
        {
            var feeds = await database.NewsFeeds.ToDictionaryAsync(row => row.CatalogId, TestContext.Current.CancellationToken);
            Assert.Equal(3, feeds.Count);
            Assert.Equal("Alpha renamed", feeds["alpha"].Name);
            Assert.Equal(["dotnet"], feeds["alpha"].Categories);
            Assert.True(feeds["alpha"].Enabled);
            Assert.False(feeds["beta"].Enabled);
            Assert.True(feeds["gamma"].Enabled);
            Assert.Equal(2, await database.NewsFeedEntries.CountAsync(TestContext.Current.CancellationToken));
        });
    }

    [Fact]
    public async Task Sync_of_an_unreadable_catalog_leaves_the_feeds_table_as_it_was()
    {
        //Arrange
        var time = new MutableTimeProvider(Now);
        await using var factory = Create(time, NewsFeedFixtures.Feed("alpha", "https://alpha.example/feed"));
        await factory.SyncFeedCatalogAsync();
        await File.WriteAllTextAsync(Path.Combine(factory.DataPath, "news-feeds.json"), "{ \"version\": 1, \"feeds\": [ { ", TestContext.Current.CancellationToken);
        //Act
        var applied = await factory.SyncFeedCatalogAsync();
        //Assert
        Assert.False(applied);
        await factory.WithDbAsync(async database => Assert.True((await database.NewsFeeds.SingleAsync(TestContext.Current.CancellationToken)).Enabled));
    }

    [Fact]
    public async Task Poll_removes_entries_14_days_after_first_seen_and_never_stores_older_ones()
    {
        //Arrange
        var time = new MutableTimeProvider(Now);
        await using var factory = Create(time, NewsFeedFixtures.Feed("alpha", "https://alpha.example/feed"));
        factory.FeedHttp.ServeXml("https://alpha.example/feed", NewsFeedFixtures.Rss(
            new FeedItemFixture("Fresh", "https://alpha.example/fresh", Now.AddHours(-1)), new FeedItemFixture("Ancient", "https://alpha.example/ancient", Now.AddDays(-20))));
        await factory.SyncFeedCatalogAsync();
        var first = await factory.PollFeedsOnceAsync();
        time.Advance(TimeSpan.FromDays(15));
        //Act
        var later = await factory.PollFeedsOnceAsync();
        //Assert
        Assert.Equal(1, first.NewEntries);
        Assert.Equal(0, later.NewEntries);
        Assert.Equal(1, later.RemovedEntries);
        await factory.WithDbAsync(async database => Assert.Empty(await database.NewsFeedEntries.ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Feed_leaves_out_entries_already_in_the_registry_unless_asked()
    {
        //Arrange
        var time = new MutableTimeProvider(Now);
        await using var factory = Create(time, NewsFeedFixtures.Feed("alpha", "https://alpha.example/feed"));
        factory.FeedHttp.ServeXml("https://alpha.example/feed", NewsFeedFixtures.Rss(
            new FeedItemFixture("Published primary", "http://www.news.example/story/20261114/1/?utm_source=rss", Now.AddHours(-3)),
            new FeedItemFixture("Published confirmation", "https://confirm.example/story/20261114/2", Now.AddHours(-2)),
            new FeedItemFixture("Fresh lead", "https://alpha.example/fresh", Now.AddHours(-1))));
        await factory.SyncFeedCatalogAsync();
        await factory.PollFeedsOnceAsync();
        await factory.SendAsync(HttpMethod.Post, "/api/news/issues", NewsIssueDataFactory.Issue(new DateOnly(2026, 11, 14)));
        //Act
        var hidden = await ReadAsync<NewsFeedResponse>(await factory.SendAsync(HttpMethod.Get, FeedPath));
        var all = await ReadAsync<NewsFeedResponse>(await factory.SendAsync(HttpMethod.Get, FeedPath + "?excludePublished=false"));
        //Assert
        Assert.Equal(["Fresh lead"], hidden.Entries.Select(entry => entry.Title).ToList());
        Assert.Equal(1, hidden.Total);
        Assert.Equal(3, all.Total);
    }

    [Fact]
    public async Task Feed_filters_by_category_by_any_query_term_and_by_the_window()
    {
        //Arrange
        var time = new MutableTimeProvider(Now);
        await using var factory = Create(time, NewsFeedFixtures.Feed("hardware-site", "https://hw.example/feed", "hardware", "homelab"),
            NewsFeedFixtures.Feed("dotnet-site", "https://dn.example/feed", "dotnet"));
        factory.FeedHttp.ServeXml("https://hw.example/feed", NewsFeedFixtures.Rss(
            new FeedItemFixture("Proxmox VE 9.1 released", "https://hw.example/proxmox", Now.AddHours(-2)),
            new FeedItemFixture("New ZFS release", "https://hw.example/zfs", Now.AddHours(-3)),
            new FeedItemFixture("GPU prices fall", "https://hw.example/gpu", Now.AddHours(-50), "Discount of 10% this week.")));
        factory.FeedHttp.ServeXml("https://dn.example/feed", NewsFeedFixtures.Rss(
            new FeedItemFixture(".NET 10 servicing", "https://dn.example/servicing", Now.AddHours(-1), "Also covers wireguard tunnels on Linux.")));
        await factory.SyncFeedCatalogAsync();
        await factory.PollFeedsOnceAsync();
        //Act
        var hardware = await ReadAsync<NewsFeedResponse>(await factory.SendAsync(HttpMethod.Get, FeedPath + "?category=hardware"));
        var homelab = await ReadAsync<NewsFeedResponse>(await factory.SendAsync(HttpMethod.Get, FeedPath + "?category=homelab&query=" + Uri.EscapeDataString(" |zfs| ")));
        var terms = await ReadAsync<NewsFeedResponse>(await factory.SendAsync(HttpMethod.Get, FeedPath + "?query=" + Uri.EscapeDataString("proxmox|WIREGUARD")));
        var day = await ReadAsync<NewsFeedResponse>(await factory.SendAsync(HttpMethod.Get, FeedPath + "?sinceHours=24"));
        var percent = await ReadAsync<NewsFeedResponse>(await factory.SendAsync(HttpMethod.Get, FeedPath + "?query=" + Uri.EscapeDataString("%")));
        //Assert
        Assert.Equal(["Proxmox VE 9.1 released", "New ZFS release", "GPU prices fall"], hardware.Entries.Select(entry => entry.Title).ToList());
        Assert.Equal(["New ZFS release"], homelab.Entries.Select(entry => entry.Title).ToList());
        Assert.Equal([".NET 10 servicing", "Proxmox VE 9.1 released"], terms.Entries.Select(entry => entry.Title).ToList());
        Assert.Equal(3, day.Total);
        Assert.Equal(["GPU prices fall"], percent.Entries.Select(entry => entry.Title).ToList()); // % is a character, not a wildcard.
    }

    [Fact]
    public async Task Feed_pages_follow_next_cursor_without_gaps_or_repeats_even_when_a_poll_adds_entries()
    {
        //Arrange
        var time = new MutableTimeProvider(Now);
        await using var factory = Create(time, NewsFeedFixtures.Feed("alpha", "https://alpha.example/feed"));
        var items = Enumerable.Range(1, 7).Select(number => new FeedItemFixture($"Story {number}", $"https://alpha.example/{number}", Now.AddHours(-number))).ToArray();
        factory.FeedHttp.ServeXml("https://alpha.example/feed", NewsFeedFixtures.Rss(items));
        await factory.SyncFeedCatalogAsync();
        await factory.PollFeedsOnceAsync();
        //Act
        var first = await ReadAsync<NewsFeedResponse>(await factory.SendAsync(HttpMethod.Get, FeedPath + "?limit=3"));
        time.Advance(TimeSpan.FromMinutes(5));
        factory.FeedHttp.ServeXml("https://alpha.example/feed", NewsFeedFixtures.Rss([new FeedItemFixture("Breaking", "https://alpha.example/breaking", Now.AddMinutes(4)), .. items]));
        await factory.PollFeedsOnceAsync();
        var second = await ReadAsync<NewsFeedResponse>(await factory.SendAsync(HttpMethod.Get, $"{FeedPath}?limit=3&cursor={first.NextCursor}"));
        var third = await ReadAsync<NewsFeedResponse>(await factory.SendAsync(HttpMethod.Get, $"{FeedPath}?limit=3&cursor={second.NextCursor}"));
        var fresh = await ReadAsync<NewsFeedResponse>(await factory.SendAsync(HttpMethod.Get, FeedPath + "?limit=3"));
        var forged = await factory.SendAsync(HttpMethod.Get, FeedPath + "?cursor=not-a-cursor");
        //Assert
        Assert.Equal(Enumerable.Range(1, 7).Select(number => $"Story {number}").ToList(),
            first.Entries.Concat(second.Entries).Concat(third.Entries).Select(entry => entry.Title).ToList());
        Assert.All([first, second, third], page => Assert.Equal(7, page.Total));
        Assert.NotNull(second.NextCursor);
        Assert.Null(third.NextCursor);
        Assert.Equal("Breaking", fresh.Entries[0].Title);
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Contains("cursor", (await ReadAsync<ApiErrorResponse>(forged)).Error);
    }

    [Fact]
    public async Task Feed_page_stays_under_60_KB_even_with_long_entries()
    {
        //Arrange
        var time = new MutableTimeProvider(Now);
        await using var factory = Create(time, NewsFeedFixtures.Feed("alpha", "https://alpha.example/feed"));
        var path = new string('p', 1500);
        factory.FeedHttp.ServeXml("https://alpha.example/feed", NewsFeedFixtures.Rss(Enumerable.Range(1, 100)
            .Select(number => new FeedItemFixture($"{number} " + new string('t', 450), $"https://alpha.example/{number}/{path}", Now.AddMinutes(-number),
                new string('s', 4000), $"https://alpha.example/{number}/{path}.jpg")).ToArray()));
        await factory.SyncFeedCatalogAsync();
        await factory.PollFeedsOnceAsync();
        //Act
        var response = await factory.SendAsync(HttpMethod.Get, FeedPath + "?limit=100");
        var bytes = (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Length;
        var page = await ReadAsync<NewsFeedResponse>(response);
        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(bytes < NewsFeedService.MaxPageBytes, $"{bytes} bytes");
        Assert.InRange(page.Entries.Count, 10, 99);
        Assert.Equal(100, page.Total);
        Assert.NotNull(page.NextCursor);
    }

    [Theory]
    [InlineData("sinceHours=0", "sinceHours")]
    [InlineData("sinceHours=169", "sinceHours")]
    [InlineData("limit=0", "limit")]
    [InlineData("limit=101", "limit")]
    [InlineData("category=gaming", "gaming")]
    [InlineData("query=a|b|c|d|e|f|g|h|i|j|k|l|m|n|o|p|q|r|s|t|u", "21 terms")]
    public async Task Feed_out_of_range_parameter_is_400_naming_it(string parameters, string named)
    {
        //Arrange
        await using var factory = Create(new MutableTimeProvider(Now), NewsFeedFixtures.Feed("alpha", "https://alpha.example/feed"));
        //Act
        var response = await factory.SendAsync(HttpMethod.Get, FeedPath + "?" + parameters);
        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(named, (await ReadAsync<ApiErrorResponse>(response)).Error);
    }

    [Fact]
    public async Task Entry_returns_the_whole_summary_and_an_unknown_id_is_a_bare_404()
    {
        //Arrange
        var time = new MutableTimeProvider(Now);
        await using var factory = Create(time, NewsFeedFixtures.Feed("alpha", "https://alpha.example/feed"));
        var text = string.Concat(Enumerable.Repeat("Fact. ", 200)).Trim();
        factory.FeedHttp.ServeXml("https://alpha.example/feed", NewsFeedFixtures.Rss(new FeedItemFixture("Story", "https://alpha.example/story", Now.AddHours(-1), text)));
        await factory.SyncFeedCatalogAsync();
        await factory.PollFeedsOnceAsync();
        var listed = (await ReadAsync<NewsFeedResponse>(await factory.SendAsync(HttpMethod.Get, FeedPath))).Entries.Single();
        //Act
        var response = await factory.SendAsync(HttpMethod.Get, $"{FeedPath}/entries/{listed.Id}");
        var missing = await factory.SendAsync(HttpMethod.Get, $"{FeedPath}/entries/{Guid.NewGuid()}");
        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await ReadAsync<NewsFeedEntryResponse>(response);
        Assert.Equal(text, entry.Summary);
        Assert.Equal((listed.Id, listed.FeedId, listed.Title, listed.Url, listed.PublishedUtc), (entry.Id, entry.FeedId, entry.Title, entry.Url, entry.PublishedUtc));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(0, missing.Content.Headers.ContentLength ?? 0);
    }

    [Fact]
    public async Task Feed_routes_answer_404_naming_the_setting_while_the_collector_is_off()
    {
        //Arrange
        await using var factory = Create(new MutableTimeProvider(Now), NewsFeedFixtures.Feed("alpha", "https://alpha.example/feed"));
        factory.Settings["News:FeedsEnabled"] = "false";
        //Act
        var list = await factory.SendAsync(HttpMethod.Get, FeedPath);
        var entry = await factory.SendAsync(HttpMethod.Get, $"{FeedPath}/entries/{Guid.NewGuid()}");
        //Assert
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Contains("News:FeedsEnabled=false", (await ReadAsync<ApiErrorResponse>(list)).Error);
        Assert.Equal(HttpStatusCode.NotFound, entry.StatusCode);
        Assert.Contains("News:FeedsEnabled=false", (await ReadAsync<ApiErrorResponse>(entry)).Error);
    }

    [Fact]
    public async Task Feed_without_the_api_key_is_401()
    {
        //Arrange
        await using var factory = Create(new MutableTimeProvider(Now), NewsFeedFixtures.Feed("alpha", "https://alpha.example/feed"));
        //Act
        var response = await factory.SendAsync(HttpMethod.Get, FeedPath, authenticated: false);
        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static GatewayApiFactory Create(MutableTimeProvider time, params NewsFeedCatalogFeed[] feeds)
    {
        var factory = new GatewayApiFactory { Time = time };
        factory.Settings["News:FeedCatalogPath"] = NewsFeedFixtures.WriteCatalog(factory.DataPath, feeds);
        return factory;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<T>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken))!;
}
