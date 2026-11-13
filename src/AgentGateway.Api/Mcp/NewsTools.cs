using System.ComponentModel;
using System.Globalization;
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
    public static readonly string[] All = [HistoryTool, CheckTool, SubmitTool, ReplaceTool, StatusTool];

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
}
