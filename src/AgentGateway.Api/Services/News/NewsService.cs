using FluentValidation;
using FluentValidation.Results;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using AgentGateway.Api.Contracts.News;
using AgentGateway.Api.Mapping;
using AgentGateway.Api.Services.Delivery;
using AgentGateway.Core.Entities;
using AgentGateway.Core.Enums;
using AgentGateway.Core.News;
using AgentGateway.Infrastructure.Persistence;

namespace AgentGateway.Api.Services.News;

/// <summary>Accepts issues into the registry and queues their publication; the HTTP API and the MCP tools both adapt this service.</summary>
public sealed class NewsService(
    ApplicationDbContext database, IValidator<NewsIssueRequest> validator, NewsEditorialGate gate, NewsDuplicateFinder duplicates,
    INewsMapper mapper, DeliveryService deliveries, TimeProvider time)
{
    public const int MaxHistoryDays = 180;
    public const int MaxCheckValues = 200;
    public const string FixtureTitle = "FIXTURE — not real news";

    public async Task<NewsSubmitResult> SubmitAsync(NewsIssueRequest request, CancellationToken cancellationToken)
    {
        if (await ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var document = NewsJson.Serialize(request);
        var hash = NewsJson.Hash(document);
        var existing = await Query().SingleOrDefaultAsync(row => row.Date == request.Date, cancellationToken);
        if (existing is not null) return await ExistingAsync(existing, hash, cancellationToken);
        if (await CheckContentAsync(request, null, cancellationToken) is { } rejected) return rejected;
        var now = time.GetUtcNow().UtcDateTime;
        var issue = new NewsIssueEntity
        {
            CreatedUtc = now, UpdatedUtc = now, SubmittedUtc = now, Revision = 1, Status = NewsPublicationStatus.Pending,
            Document = document, PayloadHash = hash
        };
        mapper.ApplyContent(issue, request, now);
        database.NewsIssues.Add(issue);
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            // A concurrent submission won the unique date, URL or eventKey; report against what is stored now.
            database.ChangeTracker.Clear();
            var concurrent = await Query().SingleOrDefaultAsync(row => row.Date == request.Date, cancellationToken);
            if (concurrent is not null) return await ExistingAsync(concurrent, hash, cancellationToken);
            return await CheckContentAsync(request, null, cancellationToken)
                ?? new(NewsSubmitStatus.Conflict, Error: "Another submission changed the news registry at the same time. Submit the issue again.");
        }
        return new(NewsSubmitStatus.Accepted, mapper.ToResponse(issue, null));
    }

    public async Task<NewsSubmitResult> ReplaceAsync(DateOnly date, NewsIssueRequest request, CancellationToken cancellationToken)
    {
        if (await ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        if (request.Date != date)
            return new(NewsSubmitStatus.Invalid, Error: $"The issue date {request.Date:yyyy-MM-dd} does not match the date {date:yyyy-MM-dd} in the route.");
        var stored = await Query().SingleOrDefaultAsync(row => row.Date == date, cancellationToken);
        if (stored is null) return new(NewsSubmitStatus.NotFound);
        if (stored.DeleteRequestedUtc is not null) return Deleting(date);
        var document = NewsJson.Serialize(request);
        var hash = NewsJson.Hash(document);
        if (stored.PayloadHash == hash) return new(NewsSubmitStatus.Existing, await ToResponseAsync(stored, cancellationToken));
        if (await CheckContentAsync(request, stored.Id, cancellationToken) is { } rejected) return rejected;
        var now = time.GetUtcNow().UtcDateTime;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // The conditional write takes SQLite's write lock first, so the worker cannot claim the old revision meanwhile.
        var locked = await database.NewsIssues.Where(row => row.Id == stored.Id && row.Status != NewsPublicationStatus.Publishing)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedUtc, now), cancellationToken);
        if (locked == 0)
            return new(NewsSubmitStatus.Conflict, Error: "This issue is being published right now. Replace it after get_news_issue_status shows it finished.");
        var issue = await database.NewsIssues.Include(row => row.Items).Include(row => row.Releases)
            .Include(row => row.SecurityAlerts).Include(row => row.Removals).SingleAsync(row => row.Id == stored.Id, cancellationToken);
        var keys = request.Items.Select(item => item.EventKey).ToHashSet();
        // A dropped item may already be a post on the site; its removal is queued after the new items are sent.
        var dropped = issue.Items.Where(item => !keys.Contains(item.EventKey)
                && (item.Status == NewsItemStatus.Published || item.WpPostId is not null || item.Attempts > 0))
            .Select(item => item.EventKey).Where(key => issue.Removals.All(removal => removal.EventKey != key)).ToList();
        database.NewsRemovals.RemoveRange(issue.Removals.Where(removal => keys.Contains(removal.EventKey)));
        database.NewsItems.RemoveRange(issue.Items);
        database.NewsReleases.RemoveRange(issue.Releases);
        database.NewsSecurityAlerts.RemoveRange(issue.SecurityAlerts);
        // Deleting first frees the unique URL and eventKey indexes for the new rows of the same issue.
        await database.SaveChangesAsync(cancellationToken);
        mapper.ApplyContent(issue, request, now);
        // Preassigned GUID keys must enter the Added state explicitly; discovery through a navigation would assume Modified.
        database.NewsItems.AddRange(issue.Items);
        database.NewsReleases.AddRange(issue.Releases);
        database.NewsSecurityAlerts.AddRange(issue.SecurityAlerts);
        database.NewsRemovals.AddRange(dropped.Select(key => new NewsRemovalEntity
        {
            IssueId = issue.Id, EventKey = key, CreatedUtc = now, Status = NewsItemStatus.Pending
        }));
        issue.Document = document;
        issue.PayloadHash = hash;
        issue.Revision++;
        issue.SubmittedUtc = now;
        issue.UpdatedUtc = now;
        issue.Status = NewsPublicationStatus.Pending;
        issue.ErrorCode = null;
        issue.PublishedUtc = null;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        database.ChangeTracker.Clear();
        var replaced = await Query().SingleAsync(row => row.Id == issue.Id, cancellationToken);
        return new(NewsSubmitStatus.Accepted, await ToResponseAsync(replaced, cancellationToken));
    }

    public async Task<NewsIssueStatusResponse?> GetStatusAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var issue = await Query().SingleOrDefaultAsync(row => row.Date == date, cancellationToken);
        return issue is null ? null : await ToResponseAsync(issue, cancellationToken);
    }

    public async Task<NewsSubmitResult> RepublishAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var issue = await Query().SingleOrDefaultAsync(row => row.Date == date, cancellationToken);
        if (issue is null) return new(NewsSubmitStatus.NotFound);
        if (issue.Status is not (NewsPublicationStatus.Failed or NewsPublicationStatus.PartiallyPublished))
            return new(NewsSubmitStatus.Existing, await ToResponseAsync(issue, cancellationToken));
        var now = time.GetUtcNow().UtcDateTime;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Only the unfinished calls run again: published items keep their state; failed ones go back to Pending.
        await database.NewsItems.Where(row => row.IssueId == issue.Id && row.Status == NewsItemStatus.Failed)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, NewsItemStatus.Pending)
                .SetProperty(row => row.ErrorCode, (string?)null), cancellationToken);
        await database.NewsRemovals.Where(row => row.IssueId == issue.Id && row.Status == NewsItemStatus.Failed)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, NewsItemStatus.Pending)
                .SetProperty(row => row.ErrorCode, (string?)null), cancellationToken);
        await database.NewsIssues.Where(row => row.Id == issue.Id
                && (row.Status == NewsPublicationStatus.Failed || row.Status == NewsPublicationStatus.PartiallyPublished))
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, NewsPublicationStatus.Pending)
                .SetProperty(row => row.ErrorCode, (string?)null).SetProperty(row => row.UpdatedUtc, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var queued = await Query().SingleAsync(row => row.Id == issue.Id, cancellationToken);
        return new(NewsSubmitStatus.Accepted, await ToResponseAsync(queued, cancellationToken));
    }

    /// <summary>
    /// Removes the test fixture (examples/news-sample-issue.json) so the live check can run again: its items leave the registry
    /// now and the worker trashes their posts. Real issues are refused; the registry of real news is never edited over HTTP.
    /// </summary>
    public async Task<NewsSubmitResult> DeleteFixtureAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var stored = await Query().SingleOrDefaultAsync(row => row.Date == date, cancellationToken);
        if (stored is null) return new(NewsSubmitStatus.NotFound);
        if (stored.Title != FixtureTitle || stored.Items.Any(item => !item.EventKey.StartsWith("fixture-", StringComparison.Ordinal)))
            return new(NewsSubmitStatus.Conflict, Error: $"Only the test fixture can be deleted: title '{FixtureTitle}' and every eventKey starting with 'fixture-'.");
        var now = time.GetUtcNow().UtcDateTime;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var locked = await database.NewsIssues.Where(row => row.Id == stored.Id && row.Status != NewsPublicationStatus.Publishing)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedUtc, now), cancellationToken);
        if (locked == 0) return new(NewsSubmitStatus.Conflict, Error: "The fixture is being published right now. Delete it after it finished.");
        var issue = await database.NewsIssues.Include(row => row.Items).Include(row => row.Releases)
            .Include(row => row.SecurityAlerts).Include(row => row.Removals).SingleAsync(row => row.Id == stored.Id, cancellationToken);
        foreach (var removal in issue.Removals.Where(removal => removal.Status == NewsItemStatus.Failed))
        {
            removal.Status = NewsItemStatus.Pending; // A repeated cleanup retries the removals that failed last time.
            removal.ErrorCode = null;
        }
        database.NewsRemovals.AddRange(issue.Items.Select(item => item.EventKey)
            .Where(key => issue.Removals.All(removal => removal.EventKey != key))
            .Select(key => new NewsRemovalEntity { IssueId = issue.Id, EventKey = key, CreatedUtc = now, Status = NewsItemStatus.Pending }));
        database.NewsItems.RemoveRange(issue.Items);
        database.NewsReleases.RemoveRange(issue.Releases);
        database.NewsSecurityAlerts.RemoveRange(issue.SecurityAlerts);
        issue.DeleteRequestedUtc = now;
        issue.Status = NewsPublicationStatus.Pending;
        issue.ErrorCode = null;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        database.ChangeTracker.Clear();
        return new(NewsSubmitStatus.Accepted, await ToResponseAsync(await Query().SingleAsync(row => row.Id == issue.Id, cancellationToken), cancellationToken));
    }

    public async Task<NewsHistoryResponse?> GetHistoryAsync(int days, CancellationToken cancellationToken)
    {
        if (days is < 1 or > MaxHistoryDays) return null;
        var from = NewsCalendar.Today(time).AddDays(1 - days);
        var items = await database.NewsItems.AsNoTracking()
            .Join(database.NewsIssues.Where(issue => issue.Date >= from), item => item.IssueId, issue => issue.Id,
                (item, issue) => new NewsHistoryItemResponse(issue.Date, item.Number, item.Category, item.Title, item.EventKey, item.CanonicalUrl))
            .ToListAsync(cancellationToken);
        var releases = await database.NewsReleases.AsNoTracking()
            .Join(database.NewsIssues.Where(issue => issue.Date >= from), release => release.IssueId, issue => issue.Id,
                (release, issue) => new NewsHistoryEntryResponse(issue.Date, "release", release.Name, release.CanonicalUrl))
            .ToListAsync(cancellationToken);
        var alerts = await database.NewsSecurityAlerts.AsNoTracking()
            .Join(database.NewsIssues.Where(issue => issue.Date >= from), alert => alert.IssueId, issue => issue.Id,
                (alert, issue) => new NewsHistoryEntryResponse(issue.Date, "securityAlert", alert.AlertId, alert.CanonicalUrl))
            .ToListAsync(cancellationToken);
        return new(days, items.OrderByDescending(item => item.Date).ThenBy(item => item.Number).ToList(),
            releases.Concat(alerts).OrderByDescending(entry => entry.Date).ThenBy(entry => entry.Kind).ThenBy(entry => entry.Name).ToList());
    }

    public async Task<(NewsCheckResponse? Response, string? Error)> CheckAsync(NewsCheckRequest request, CancellationToken cancellationToken)
    {
        var urls = request.Urls ?? [];
        var keys = request.EventKeys ?? [];
        if (urls.Count > MaxCheckValues || keys.Count > MaxCheckValues)
            return (null, $"Send at most {MaxCheckValues} urls and {MaxCheckValues} eventKeys per check.");
        var canonical = new Dictionary<string, List<string>>();
        foreach (var url in urls)
        {
            if (CanonicalUrl.Normalize(url) is not { } normalized) return (null, $"'{Shorten(url)}' is not an absolute http(s) URL.");
            if (!canonical.TryGetValue(normalized, out var originals)) canonical[normalized] = originals = [];
            originals.Add(url);
        }
        var lookup = canonical.Keys.ToList();
        var lookupKeys = keys.Where(key => key is not null).Distinct().ToList();
        var known = new List<NewsKnownResponse>();
        var items = await database.NewsItems.AsNoTracking()
            .Where(item => lookup.Contains(item.CanonicalUrl) || lookupKeys.Contains(item.EventKey))
            .Join(database.NewsIssues, item => item.IssueId, issue => issue.Id,
                (item, issue) => new { item.CanonicalUrl, item.EventKey, item.Number, item.Title, issue.Date })
            .ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            if (canonical.TryGetValue(item.CanonicalUrl, out var originals))
                known.AddRange(originals.Select(value => new NewsKnownResponse(value, "url", item.Date, item.Number, item.Title)));
            if (lookupKeys.Contains(item.EventKey)) known.Add(new(item.EventKey, "eventKey", item.Date, item.Number, item.Title));
        }
        var extras = await database.NewsSources.AsNoTracking().Where(source => lookup.Contains(source.CanonicalUrl))
            .Join(database.NewsItems, source => source.ItemId, item => item.Id, (source, item) => new { source.CanonicalUrl, item.IssueId, item.Number, item.Title })
            .Join(database.NewsIssues, row => row.IssueId, issue => issue.Id, (row, issue) => new { row.CanonicalUrl, row.Number, row.Title, issue.Date })
            .ToListAsync(cancellationToken);
        known.AddRange(extras.SelectMany(row => canonical[row.CanonicalUrl].Select(value => new NewsKnownResponse(value, "extraSource", row.Date, row.Number, row.Title))));
        var releases = await database.NewsReleases.AsNoTracking().Where(release => lookup.Contains(release.CanonicalUrl))
            .Join(database.NewsIssues, release => release.IssueId, issue => issue.Id, (release, issue) => new { release.CanonicalUrl, release.Name, issue.Date })
            .ToListAsync(cancellationToken);
        known.AddRange(releases.SelectMany(row => canonical[row.CanonicalUrl].Select(value => new NewsKnownResponse(value, "release", row.Date, null, row.Name))));
        var alerts = await database.NewsSecurityAlerts.AsNoTracking().Where(alert => lookup.Contains(alert.CanonicalUrl))
            .Join(database.NewsIssues, alert => alert.IssueId, issue => issue.Id, (alert, issue) => new { alert.CanonicalUrl, alert.AlertId, issue.Date })
            .ToListAsync(cancellationToken);
        known.AddRange(alerts.SelectMany(row => canonical[row.CanonicalUrl].Select(value => new NewsKnownResponse(value, "securityAlert", row.Date, null, row.AlertId))));
        return (new(known), null);
    }

    private IQueryable<NewsIssueEntity> Query() => database.NewsIssues.AsNoTracking().AsSplitQuery()
        .Include(row => row.Items).Include(row => row.Releases).Include(row => row.SecurityAlerts).Include(row => row.Removals);

    private async Task<NewsIssueStatusResponse> ToResponseAsync(NewsIssueEntity issue, CancellationToken cancellationToken)
        => mapper.ToResponse(issue, issue.NoticeDeliveryId is { } notice ? await deliveries.GetAsync(notice, cancellationToken) : null);

    private async Task<NewsSubmitResult> ExistingAsync(NewsIssueEntity existing, string hash, CancellationToken cancellationToken)
        => existing.DeleteRequestedUtc is not null ? Deleting(existing.Date)
        : existing.PayloadHash == hash
            ? new(NewsSubmitStatus.Existing, await ToResponseAsync(existing, cancellationToken))
            : new(NewsSubmitStatus.Conflict, Error: $"An issue for {existing.Date:yyyy-MM-dd} already exists with different content. "
                + $"Replace it with PUT /api/news/issues/{existing.Date:yyyy-MM-dd} (MCP: replace_news_issue).");

    private static NewsSubmitResult Deleting(DateOnly date) => new(NewsSubmitStatus.Conflict,
        Error: $"The issue for {date:yyyy-MM-dd} is being deleted. Wait until its status returns 404.");

    private async Task<NewsSubmitResult?> ValidateAsync(NewsIssueRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return new(NewsSubmitStatus.Invalid, Error: "The news issue is missing.");
        var validation = await validator.ValidateAsync(request, cancellationToken);
        return validation.IsValid ? null : new(NewsSubmitStatus.Invalid, Error: "The news issue does not match contract v1.",
            Errors: validation.Errors.Select(Describe).ToList());
    }

    private async Task<NewsSubmitResult?> CheckContentAsync(NewsIssueRequest request, Guid? ownIssueId, CancellationToken cancellationToken)
    {
        var violations = gate.Check(request);
        if (violations.Count > 0)
            return new(NewsSubmitStatus.Rejected, Error: "The issue breaks the editorial rules. Fix exactly the listed violations and submit again.",
                Violations: violations);
        var found = await duplicates.FindAsync(request, ownIssueId, cancellationToken);
        return found.Count == 0 ? null : new(NewsSubmitStatus.Duplicate,
            Error: "Some items were already published or repeat inside the issue. Replace exactly the listed items and submit again.",
            Duplicates: found);
    }

    /// <summary>FluentValidation paths are PascalCase; callers send camelCase JSON, so the paths are reported the same way.</summary>
    private static string Describe(ValidationFailure failure)
    {
        var path = string.Join('.', failure.PropertyName.Split('.')
            .Select(segment => segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
        return $"{path}: {failure.ErrorMessage}";
    }

    private static string Shorten(string? value) => value is null ? "" : value.Length > 100 ? value[..100] + "…" : value;
}
