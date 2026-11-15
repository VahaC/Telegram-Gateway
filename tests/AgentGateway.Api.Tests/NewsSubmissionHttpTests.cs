using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using AgentGateway.Api.Contracts.News;
using AgentGateway.Core.Enums;
using AgentGateway.TestInfrastructure.Http;
using AgentGateway.TestInfrastructure.News;

namespace AgentGateway.Api.Tests;

public sealed class NewsSubmissionHttpTests
{
    private const string Issues = "/api/news/issues";
    private static readonly DateOnly Date = new(2026, 11, 14);
    private static readonly DateOnly EarlierDate = new(2026, 11, 13);

    [Fact]
    public async Task Post_valid_issue_is_accepted_and_every_item_enters_the_registry()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        var issue = NewsIssueDataFactory.Issue(Date);
        //Act
        var response = await factory.SendAsync(HttpMethod.Post, Issues, issue);
        var status = await ReadAsync<NewsIssueStatusResponse>(response);
        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("/api/news/issues/2026-11-14", response.Headers.Location!.OriginalString);
        Assert.Equal(NewsPublicationStatus.Pending, status.Status);
        Assert.Equal(1, status.Revision);
        Assert.Equal(38, status.ItemCount);
        Assert.Equal(7, status.Categories["dotnet"]);
        Assert.All(status.Items, item => Assert.Equal(NewsItemStatus.Pending, item.Status));
        await factory.WithDbAsync(async database =>
        {
            var stored = await database.NewsIssues.Include(row => row.Items).ThenInclude(item => item.ExtraSources)
                .Include(row => row.Releases).Include(row => row.SecurityAlerts).SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(38, stored.Items.Count);
            Assert.Equal(2, stored.Releases.Count);
            Assert.Single(stored.SecurityAlerts);
            Assert.Equal("https://news.example/story/20261114/7", stored.Items.Single(item => item.Number == 7).CanonicalUrl);
            Assert.Contains("\"eventKey\":\"story-20261114-7\"", stored.Document);
            Assert.Contains("Новина 7", stored.Document); // Stored as readable UTF-8, not \u escapes.
            Assert.Equal(64, stored.PayloadHash.Length);
            Assert.Equal(["test", "homelab"], stored.Items.Single(item => item.Number == 2).Tags);
            Assert.Single(stored.Items.Single(item => item.Number == 2).ExtraSources);
        });
        Assert.Empty(factory.WordPressHttp.Requests); // Publication happens in the worker, never in the request.
    }

    [Fact]
    public async Task Post_identical_repeat_returns_existing_issue_and_different_content_is_conflict()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        var issue = NewsIssueDataFactory.Issue(Date);
        await factory.SendAsync(HttpMethod.Post, Issues, issue);
        //Act
        var repeat = await factory.SendAsync(HttpMethod.Post, Issues, issue);
        var changed = await factory.SendAsync(HttpMethod.Post, Issues, issue with { Title = "Інший заголовок" });
        //Assert
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal(1, (await ReadAsync<NewsIssueStatusResponse>(repeat)).Revision);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Contains("PUT /api/news/issues/2026-11-14", (await ReadAsync<NewsRejectionResponse>(changed)).Error);
        await factory.WithDbAsync(async database => Assert.Single(await database.NewsIssues.ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Post_structure_errors_return_400_with_camel_case_paths()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        var issue = NewsIssueDataFactory.Issue(Date).WithItems(item => item.Number switch
        {
            1 => item with { EventKey = "Not A Key" },
            2 => item with { Summary = "<b>bold</b> text" },
            3 => item with { Category = "gaming" },
            4 => item with { Image = new("http://images.example/plain-http.jpg", null) },
            _ => item
        }) with { ContractVersion = 2 };
        //Act
        var response = await factory.SendAsync(HttpMethod.Post, Issues, issue);
        var rejection = await ReadAsync<NewsRejectionResponse>(response);
        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(rejection.Errors!, error => error.StartsWith("contractVersion:", StringComparison.Ordinal));
        Assert.Contains(rejection.Errors!, error => error.StartsWith("items[0].eventKey:", StringComparison.Ordinal));
        Assert.Contains(rejection.Errors!, error => error.StartsWith("items[1].summary:", StringComparison.Ordinal));
        Assert.Contains(rejection.Errors!, error => error.StartsWith("items[2].category:", StringComparison.Ordinal));
        Assert.Contains(rejection.Errors!, error => error.StartsWith("items[3].image.url:", StringComparison.Ordinal));
        await factory.WithDbAsync(async database => Assert.Empty(await database.NewsIssues.ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Post_unknown_property_returns_400_naming_it()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        using var request = new HttpRequestMessage(HttpMethod.Post, Issues);
        request.Headers.Add("X-Api-Key", GatewayApiFactory.ApiKey);
        request.Content = new StringContent("{\"contractVersion\":1,\"date\":\"2026-11-14\",\"headline\":\"x\"}", Encoding.UTF8, "application/json");
        //Act
        var response = await factory.Client.SendAsync(request, TestContext.Current.CancellationToken);
        var rejection = await ReadAsync<NewsRejectionResponse>(response);
        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("headline", Assert.Single(rejection.Errors!));
    }

    [Theory]
    [InlineData("min_items")]
    [InlineData("category_min")]
    [InlineData("ai_share")]
    [InlineData("top_count")]
    [InlineData("top_numbering")]
    [InlineData("numbering")]
    public async Task Post_editorial_violation_returns_422_with_exactly_that_violation(string code)
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        // The per-category minimum is off by default since 2026-11-15; the rule itself still works when configured.
        if (code == "category_min") factory.Settings["News:MinPerCategory"] = "1";
        var issue = code switch
        {
            "min_items" => NewsIssueDataFactory.Issue(Date, count: 4),
            "category_min" => NewsIssueDataFactory.Issue(Date).WithItems(item => item.Category == "smarthome" ? item with { Category = "dotnet" } : item),
            // 40 items, 13 of them AI: 32.5%.
            "ai_share" => NewsIssueDataFactory.Issue(Date, count: 40).WithItems(item => item.Number is 7 or 8 or 9 or 10 or 11 or 13 or 14 ? item with { Category = "ai" } : item),
            "top_count" => NewsIssueDataFactory.Issue(Date).WithItems(item => item.Number == 5 ? item with { IsTop = false } : item),
            "top_numbering" => NewsIssueDataFactory.Issue(Date).WithItems(item => item with { IsTop = item.Number is >= 3 and <= 7 }),
            _ => NewsIssueDataFactory.Issue(Date).WithItems(item => item.Number == 20 ? item with { Number = 40 } : item)
        };
        //Act
        var response = await factory.SendAsync(HttpMethod.Post, Issues, issue);
        var rejection = await ReadAsync<NewsRejectionResponse>(response);
        //Assert
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var violation = Assert.Single(rejection.Violations!);
        Assert.StartsWith(code + ":", violation);
        if (code == "ai_share") Assert.Contains("13 of 40 items (32.5%)", violation);
        if (code == "min_items") Assert.Contains("has 4 news items; at least 5 are required", violation);
        await factory.WithDbAsync(async database => Assert.Empty(await database.NewsItems.ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Post_41_items_is_422_max_items_with_the_default_gate()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        //Act
        var response = await factory.SendAsync(HttpMethod.Post, Issues, NewsIssueDataFactory.Issue(Date, count: 41));
        var rejection = await ReadAsync<NewsRejectionResponse>(response);
        //Assert
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal("max_items: The issue has 41 news items; at most 40 are allowed.", Assert.Single(rejection.Violations!));
        await factory.WithDbAsync(async database => Assert.Empty(await database.NewsIssues.ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Post_5_items_with_an_empty_category_is_accepted_by_the_default_gate()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        var issue = NewsIssueDataFactory.Issue(Date, count: 5);
        //Act
        var response = await factory.SendAsync(HttpMethod.Post, Issues, issue);
        var status = await ReadAsync<NewsIssueStatusResponse>(response);
        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain(issue.Items, item => item.Category == "ai");
        Assert.Equal(5, status.ItemCount);
        await factory.WithDbAsync(async database => Assert.Equal(5, await database.NewsItems.CountAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Post_same_url_with_tracking_parameters_on_earlier_day_is_409_naming_that_day_and_item()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        await factory.SendAsync(HttpMethod.Post, Issues, NewsIssueDataFactory.Issue(EarlierDate, prefix: "old"));
        var issue = NewsIssueDataFactory.Issue(Date).WithItems(item => item.Number == 7
            ? item with { Source = new("http://www.news.example/old/20261113/3/?utm_source=rss&utm_medium=feed#comments", "Same article") }
            : item);
        //Act
        var response = await factory.SendAsync(HttpMethod.Post, Issues, issue);
        var rejection = await ReadAsync<NewsRejectionResponse>(response);
        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var duplicate = Assert.Single(rejection.Duplicates!);
        Assert.Equal("items[6].source.url", duplicate.Path);
        Assert.Equal("https://news.example/old/20261113/3", duplicate.CanonicalUrl);
        Assert.Equal(EarlierDate, duplicate.IssueDate);
        Assert.Equal(3, duplicate.ItemNumber);
        Assert.Equal("Новина 3", duplicate.Title);
        await factory.WithDbAsync(async database => Assert.Single(await database.NewsIssues.ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Post_same_event_key_from_another_site_is_409()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        await factory.SendAsync(HttpMethod.Post, Issues, NewsIssueDataFactory.Issue(EarlierDate, prefix: "old"));
        var issue = NewsIssueDataFactory.Issue(Date).WithItems(item => item.Number == 9 ? item with { EventKey = "old-20261113-4" } : item);
        //Act
        var response = await factory.SendAsync(HttpMethod.Post, Issues, issue);
        var duplicate = Assert.Single((await ReadAsync<NewsRejectionResponse>(response)).Duplicates!);
        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("items[8].eventKey", duplicate.Path);
        Assert.Equal("old-20261113-4", duplicate.EventKey);
        Assert.Null(duplicate.CanonicalUrl);
        Assert.Equal(EarlierDate, duplicate.IssueDate);
        Assert.Equal(4, duplicate.ItemNumber);
    }

    [Fact]
    public async Task Post_duplicates_inside_one_issue_and_extra_source_of_earlier_item_are_409()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        await factory.SendAsync(HttpMethod.Post, Issues, NewsIssueDataFactory.Issue(EarlierDate, prefix: "old"));
        var issue = NewsIssueDataFactory.Issue(Date).WithItems(item => item.Number switch
        {
            10 => item with { Source = new("https://news.example/story/20261114/9?utm_campaign=x", "Copy") },
            12 => item with { EventKey = "story-20261114-11" },
            14 => item with { ExtraSources = [new("https://news.example/old/20261113/2", "Earlier primary")] },
            _ => item
        });
        //Act
        var response = await factory.SendAsync(HttpMethod.Post, Issues, issue);
        var duplicates = (await ReadAsync<NewsRejectionResponse>(response)).Duplicates!;
        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(3, duplicates.Count);
        Assert.Contains(duplicates, entry => entry.Path == "items[9].source.url" && entry.IssueDate == Date && entry.ItemNumber == 9);
        Assert.Contains(duplicates, entry => entry.Path == "items[11].eventKey" && entry.IssueDate == Date && entry.ItemNumber == 11);
        Assert.Contains(duplicates, entry => entry.Path == "items[13].extraSources[0].url" && entry.IssueDate == EarlierDate && entry.ItemNumber == 2);
    }

    [Fact]
    public async Task Post_release_or_alert_seen_in_earlier_issue_is_409_but_news_item_urls_are_not_compared_with_them()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        var earlier = NewsIssueDataFactory.Issue(EarlierDate, prefix: "old");
        await factory.SendAsync(HttpMethod.Post, Issues, earlier);
        var issue = NewsIssueDataFactory.Issue(Date) with
        {
            Releases = [earlier.Releases![0] with { Url = earlier.Releases[0].Url + "?utm_source=x" }],
            SecurityAlerts = []
        };
        //Act
        var response = await factory.SendAsync(HttpMethod.Post, Issues, issue);
        var duplicate = Assert.Single((await ReadAsync<NewsRejectionResponse>(response)).Duplicates!);
        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("releases[0].url", duplicate.Path);
        Assert.Equal(EarlierDate, duplicate.IssueDate);
        Assert.Null(duplicate.ItemNumber);
    }

    [Fact]
    public async Task Put_replacement_does_not_clash_with_its_own_items_and_bumps_revision()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        var issue = NewsIssueDataFactory.Issue(Date);
        await factory.SendAsync(HttpMethod.Post, Issues, issue);
        var replacement = issue.WithItems(item => item.Number == 1 ? item with { Title = "Виправлений заголовок" } : item);
        //Act
        var response = await factory.SendAsync(HttpMethod.Put, Issues + "/2026-11-14", replacement);
        var status = await ReadAsync<NewsIssueStatusResponse>(response);
        var unchanged = await factory.SendAsync(HttpMethod.Put, Issues + "/2026-11-14", replacement);
        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(2, status.Revision);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.Equal(2, (await ReadAsync<NewsIssueStatusResponse>(unchanged)).Revision);
        await factory.WithDbAsync(async database =>
        {
            Assert.Equal(38, await database.NewsItems.CountAsync(TestContext.Current.CancellationToken));
            Assert.Equal("Виправлений заголовок", (await database.NewsItems.SingleAsync(item => item.Number == 1, TestContext.Current.CancellationToken)).Title);
            Assert.Equal(2, (await database.NewsIssues.SingleAsync(TestContext.Current.CancellationToken)).Revision);
        });
    }

    [Fact]
    public async Task Put_unknown_date_is_404_and_mismatched_date_is_400()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        var issue = NewsIssueDataFactory.Issue(Date);
        //Act
        var missing = await factory.SendAsync(HttpMethod.Put, Issues + "/2026-11-14", issue);
        var mismatch = await factory.SendAsync(HttpMethod.Put, Issues + "/2026-11-15", issue);
        //Assert
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(0, missing.Content.Headers.ContentLength ?? 0);
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
    }

    [Fact]
    public async Task History_and_check_return_what_was_stored()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var older = NewsIssueDataFactory.Issue(today.AddDays(-3), prefix: "older");
        var newer = NewsIssueDataFactory.Issue(today.AddDays(-2), prefix: "newer");
        await factory.SendAsync(HttpMethod.Post, Issues, older);
        await factory.SendAsync(HttpMethod.Post, Issues, newer);
        //Act
        var historyResponse = await factory.SendAsync(HttpMethod.Get, "/api/news/history?days=30");
        var history = await ReadAsync<NewsHistoryResponse>(historyResponse);
        var tooLong = await factory.SendAsync(HttpMethod.Get, "/api/news/history?days=181");
        var checkResponse = await factory.SendAsync(HttpMethod.Post, "/api/news/check", new NewsCheckRequest(
            [newer.Items[4].Source.Url + "?utm_source=x", older.Items[0].ExtraSources![0].Url, older.Releases![1].Url, "https://unknown.example/new"],
            [older.Items[2].EventKey, "never-seen-key"]));
        var check = await ReadAsync<NewsCheckResponse>(checkResponse);
        //Assert
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.Equal(76, history.Items.Count);
        Assert.Equal(today.AddDays(-2), history.Items[0].Date);
        Assert.Equal(1, history.Items[0].Number);
        Assert.Equal(today.AddDays(-3), history.Items[^1].Date);
        Assert.Equal("https://news.example/newer/" + today.AddDays(-2).ToString("yyyyMMdd") + "/1", history.Items[0].CanonicalUrl);
        Assert.Equal(6, history.ReleasesAndAlerts.Count);
        Assert.Contains(history.ReleasesAndAlerts, entry => entry.Kind == "securityAlert");
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, checkResponse.StatusCode);
        Assert.Equal(4, check.Known.Count);
        Assert.Contains(check.Known, known => known.Kind == "url" && known.ItemNumber == 5 && known.IssueDate == today.AddDays(-2));
        Assert.Contains(check.Known, known => known.Kind == "extraSource" && known.ItemNumber == 1);
        Assert.Contains(check.Known, known => known.Kind == "release" && known.Title == "Second older");
        Assert.Contains(check.Known, known => known.Kind == "eventKey" && known.Value == older.Items[2].EventKey && known.ItemNumber == 3);
    }

    [Fact]
    public async Task Post_issue_above_128_KiB_is_accepted_but_above_1_MiB_is_413()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        var paragraph = string.Concat(Enumerable.Repeat("Довгий український текст новини. ", 70))[..1990];
        var large = NewsIssueDataFactory.Issue(Date, count: 40).WithItems(item => item with { Summary = paragraph });
        using var huge = new HttpRequestMessage(HttpMethod.Post, Issues);
        huge.Headers.Add("X-Api-Key", GatewayApiFactory.ApiKey);
        huge.Content = new StringContent(new string('x', 1024 * 1024 + 1), Encoding.UTF8, "application/json");
        //Act
        var accepted = await factory.SendAsync(HttpMethod.Post, Issues, large);
        var tooLarge = await factory.Client.SendAsync(huge, TestContext.Current.CancellationToken);
        var notification = await factory.SendAsync(HttpMethod.Post, "/api/messages", new { text = new string('x', 130 * 1024) });
        //Assert
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.True(Encoding.UTF8.GetByteCount(System.Text.Json.JsonSerializer.Serialize(large, GatewayApiFactory.JsonOptions)) > 128 * 1024);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, notification.StatusCode);
    }

    [Fact]
    public async Task Fixture_file_is_a_valid_issue_that_uses_every_field()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        var json = RepositoryFiles.Read("examples/news-sample-issue.json");
        using var request = new HttpRequestMessage(HttpMethod.Post, Issues);
        request.Headers.Add("X-Api-Key", GatewayApiFactory.ApiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var issue = System.Text.Json.JsonSerializer.Deserialize<NewsIssueRequest>(json, GatewayApiFactory.JsonOptions)!;
        //Act
        var response = await factory.Client.SendAsync(request, TestContext.Current.CancellationToken);
        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("FIXTURE — not real news", issue.Title);
        Assert.Equal(36, issue.Items.Count);
        Assert.All(issue.Items, item => Assert.StartsWith("fixture-", item.EventKey));
        Assert.Contains(issue.Items, item => item.EventOn is not null);
        Assert.Contains(issue.Items, item => item.ReleaseStatus is not null && item.Version is not null);
        Assert.Contains(issue.Items, item => item.ExtraSources is { Count: 3 });
        Assert.Contains(issue.Items, item => item.Image is null);
        Assert.Single(issue.Items, item => item.Image?.Url.Contains("broken", StringComparison.Ordinal) == true);
        Assert.Contains(issue.SecurityAlerts!, alert => alert.Cvss is null);
        Assert.NotNull(issue.Research);
    }

    [Fact]
    public async Task News_routes_are_not_found_when_the_module_is_disabled()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.Settings["News:Enabled"] = "false";
        //Act
        var submit = await factory.SendAsync(HttpMethod.Post, Issues, NewsIssueDataFactory.Issue(Date));
        var history = await factory.SendAsync(HttpMethod.Get, "/api/news/history");
        //Assert
        Assert.Equal(HttpStatusCode.NotFound, submit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
        await factory.WithDbAsync(async database => Assert.Empty(await database.NewsIssues.ToListAsync(TestContext.Current.CancellationToken)));
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<T>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken))!;
}
