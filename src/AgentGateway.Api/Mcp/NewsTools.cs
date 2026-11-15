using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using AgentGateway.Api.Contracts.News;
using AgentGateway.Api.Services.News;

namespace AgentGateway.Api.Mcp;

/// <summary>Adapters over NewsService for AI callers; they require the news:publish scope (or the API key).</summary>
[McpServerToolType]
public sealed class NewsTools(IServiceScopeFactory scopes)
{
    public const string HistoryTool = "get_news_history";
    public const string CheckTool = "check_news_candidates";
    public const string SubmitTool = "submit_news_issue";
    public const string ReplaceTool = "replace_news_issue";
    public const string StatusTool = "get_news_issue_status";
    public const string FeedTool = "get_news_feed";
    public const string FeedEntryTool = "get_news_feed_entry";
    // Every tool listed here needs the news:publish scope and the news module (McpToolAccess).
    public static readonly string[] All = [HistoryTool, CheckTool, SubmitTool, ReplaceTool, StatusTool, FeedTool, FeedEntryTool];

    [McpServerTool(Name = HistoryTool, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Step 1 of the daily news workflow. Returns every news item, release and security alert already published in the last N days "
        + "(newest first): date, number, category, title, eventKey and canonical URL. Read it BEFORE researching, and skip events that are "
        + "already listed: the gateway refuses any item whose URL or eventKey appeared in an earlier issue.")]
    public async Task<NewsHistoryResponse?> GetHistoryAsync(
        [Description("How many days back, 1..180. Default 30.")] int days = 30, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NewsService>().GetHistoryAsync(days, cancellationToken)
            ?? throw new ArgumentOutOfRangeException(nameof(days), $"Days must be between 1 and {NewsService.MaxHistoryDays}.");
    }

    [McpServerTool(Name = CheckTool, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Step 2. Before writing the issue, check candidate source URLs and eventKeys (up to 200 each). Every entry in 'known' "
        + "was already published (with its date, item number and title): drop or replace those candidates. Tracking parameters, www, "
        + "http/https and trailing slashes do not matter: URLs are compared in canonical form.")]
    public async Task<NewsCheckResponse> CheckAsync(
        [Description("Candidate primary or confirming source URLs.")] string[]? urls = null,
        [Description("Candidate event keys, e.g. proxmox-ve-9-1-release.")] string[]? eventKeys = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var (response, error) = await scope.ServiceProvider.GetRequiredService<NewsService>()
            .CheckAsync(new NewsCheckRequest(urls, eventKeys), cancellationToken);
        return response ?? throw new ArgumentException(error);
    }

    [McpServerTool(Name = SubmitTool, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Step 3. Submit the finished issue ONCE (contract v1: date, title, language, items with number/category/isTop/importance/"
        + "title/summary/whyItMatters/publishedOn/eventKey/source/extraSources/image/tags, releases, securityAlerts, research). "
        + "Status Accepted = queued for publication to the website. Existing = this exact issue was already accepted. "
        + "Invalid = fix 'errors' (structure). Rejected = fix exactly the listed 'violations' (editorial rules: item count, categories, "
        + "AI share, TOP-5). Duplicate = replace exactly the items listed in 'duplicates' (already published earlier or repeated inside "
        + "the issue) and submit again. Conflict = an issue for that date exists: use replace_news_issue. "
        + "Then poll get_news_issue_status until Published, PartiallyPublished or Failed. The news texts are not sent to Telegram; "
        + "one line with the link to the day page is.")]
    public async Task<NewsSubmitResult> SubmitAsync(
        [Description("The whole issue document, contract v1.")] NewsIssueRequest issue, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NewsService>().SubmitAsync(issue, cancellationToken);
    }

    [McpServerTool(Name = ReplaceTool, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Replace an issue that was already accepted for this date (fixes after the fact). Same validation as submit_news_issue; "
        + "duplicates ignore the issue's own items. Bumps the revision, republishes every item and removes posts of dropped items. "
        + "Then poll get_news_issue_status.")]
    public async Task<NewsSubmitResult> ReplaceAsync(
        [Description("The issue date, yyyy-MM-dd; must equal issue.date.")] string date,
        [Description("The whole corrected issue document, contract v1.")] NewsIssueRequest issue, CancellationToken cancellationToken = default)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return new(NewsSubmitStatus.Invalid, Error: "Date must use yyyy-MM-dd.");
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NewsService>().ReplaceAsync(parsed, issue, cancellationToken);
    }

    [McpServerTool(Name = StatusTool, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Step 4. Publication status of the issue for a date (yyyy-MM-dd): Pending, Publishing, Published, PartiallyPublished or "
        + "Failed, the day page URL, per item state, URL and image result (an image WordPress could not fetch does not fail the item), "
        + "and the Telegram notice. Poll every 30-60 seconds until Published; PartiallyPublished or Failed need the owner. "
        + "An empty result means no issue exists for that date.")]
    public async Task<NewsIssueStatusResponse?> GetStatusAsync(string date, CancellationToken cancellationToken = default)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new ArgumentException("Date must use yyyy-MM-dd.");
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NewsService>().GetStatusAsync(parsed, cancellationToken);
    }

    [McpServerTool(Name = FeedTool, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Leads for the issue: entries the gateway collected from the publishers' own RSS/Atom feeds, newest first. Each entry has "
        + "id, feedId, feedName, feedCategories (hints, not a verdict), kind (vendor, release, news, blog, advisory, community, aggregator), "
        + "title, url, publishedUtc, firstSeenUtc, a summary cut to 300 characters and imageUrl (https, taken from the feed). "
        + "publishedUtc is the publication date from the publisher's own feed, so it counts as verified; when it is null the feed gave no "
        + "date and firstSeenUtc is when the gateway first saw the entry. Facts beyond the summary need get_news_feed_entry, the source "
        + "page or another source. Narrow the list with category and with query, e.g. 'Proxmox|ZFS|WireGuard' (any term, case-insensitive "
        + "for Latin letters, in title or summary) instead of paging through thousands of entries. excludePublished (default true) drops "
        + "entries already in the news registry, the same check as check_news_candidates. Follow nextCursor only while you need more (it is null on the "
        + "last page); a page stays under 60 KB, so it may hold fewer entries than limit. health counts enabled and failing feeds and the last successful poll.")]
    public async Task<NewsFeedResponse> GetFeedAsync(
        [Description("Look back this many hours, 1..168. Default 168 (7 days).")] int sinceHours = NewsFeedService.MaxSinceHours,
        [Description("Optional: only feeds whose category hints include dotnet, homelab, smarthome, hardware, security or ai.")] string? category = null,
        [Description("Optional: up to 20 alternatives separated by |; an entry matches when its title or summary contains any of them.")] string? query = null,
        [Description("Default true: leave out entries whose URL is already in the news registry.")] bool excludePublished = true,
        [Description("Entries per page, 1..100. Default 50.")] int limit = 50,
        [Description("nextCursor from the previous page; omit for the first page.")] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<NewsFeedService>()
            .GetFeedAsync(sinceHours, category, query, excludePublished, limit, cursor, cancellationToken);
        // McpException is the one exception whose message the SDK hands to the caller; the agent needs it to fix the call.
        return result.Value ?? throw new McpException(result.Error!);
    }

    [McpServerTool(Name = FeedEntryTool, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("One entry of get_news_feed by id, with the whole summary as the publisher's feed carried it (plain text, up to 4000 "
        + "characters). The publication date comes from the publisher's own feed; facts that are not in this text need the source page "
        + "or another source. An empty result means no entry with that id (entries are kept 14 days).")]
    public async Task<NewsFeedEntryResponse?> GetFeedEntryAsync(
        [Description("The entry id from get_news_feed.")] string id, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(id, out var parsed)) throw new McpException($"id '{(id.Length > 40 ? id[..40] : id)}' is not an entry id from get_news_feed.");
        using var scope = scopes.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<NewsFeedService>().GetEntryAsync(parsed, cancellationToken);
        return result.Status switch
        {
            NewsFeedQueryStatus.Ok => result.Value,
            NewsFeedQueryStatus.NotFound => null,
            _ => throw new McpException(result.Error!)
        };
    }
}
