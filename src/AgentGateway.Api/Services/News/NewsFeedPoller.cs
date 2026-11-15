using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AgentGateway.Core.Abstractions;
using AgentGateway.Core.Entities;
using AgentGateway.Core.News;
using AgentGateway.Core.Options;
using AgentGateway.Infrastructure.Persistence;

namespace AgentGateway.Api.Services.News;

/// <summary>
/// One poll of every enabled feed (ADR 0004). Hosts are polled in parallel (News:FeedParallelism) but each host sees one
/// request at a time: a third of the catalog is github.com, and a burst of 250 requests is not what a feed reader does.
/// After a 429 the rest of that host's feeds wait for the next run. Results are written one feed at a time, because a
/// DbContext is not thread-safe and SQLite has one writer anyway. A broken feed or entry is recorded and the run goes on.
/// </summary>
public sealed class NewsFeedPoller(ApplicationDbContext database, INewsFeedClient client, IOptions<NewsOptions> options,
    TimeProvider time, ILogger<NewsFeedPoller> logger)
{
    /// <summary>The feed tools look back at most 168 hours; two weeks keep re-announced articles recognisable at half the rows of a month.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(14);
    /// <summary>Recorded on the feeds a run did not request because their host had just answered 429.</summary>
    public const string SkippedAfterRateLimit = "skipped_host_429";
    /// <summary>Keeps the summary line short when a network outage fails every feed at once.</summary>
    private const int MaxLoggedFailures = 10;

    public async Task<NewsFeedPollSummary> PollOnceAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var feeds = await database.NewsFeeds.AsNoTracking().Where(feed => feed.Enabled).OrderBy(feed => feed.CatalogId).ToListAsync(cancellationToken);
        var results = new NewsFeedFetchResult[feeds.Count];
        // The largest host starts first, so its serial queue does not become the tail of the run.
        var hosts = Enumerable.Range(0, feeds.Count).GroupBy(index => HostOf(feeds[index].Url)).OrderByDescending(group => group.Count()).ToList();
        await Parallel.ForEachAsync(hosts,
            new ParallelOptions { MaxDegreeOfParallelism = options.Value.FeedParallelism, CancellationToken = cancellationToken },
            async (host, token) =>
            {
                var throttled = false;
                foreach (var index in host)
                {
                    // No retry and no further request after a 429: the host asked for less, not for the same again.
                    results[index] = throttled ? NewsFeedFetchResult.Failed(SkippedAfterRateLimit) : await FetchAsync(feeds[index], token);
                    throttled |= results[index].ErrorCode == "http_429";
                }
            });
        int succeeded = 0, notModified = 0, newEntries = 0;
        var failures = new List<string>();
        for (var index = 0; index < feeds.Count; index++)
        {
            var (feed, result) = (feeds[index], results[index]);
            try
            {
                if (result.Status == NewsFeedFetchStatus.Fetched) newEntries += await AddEntriesAsync(feed, result.Entries, now, cancellationToken);
                await RecordAsync(feed, result, now, cancellationToken);
                if (result.Status == NewsFeedFetchStatus.Failed) failures.Add($"{feed.CatalogId} {result.ErrorCode}");
                else succeeded++;
                if (result.Status == NewsFeedFetchStatus.NotModified) notModified++;
            }
            catch (DbUpdateException exception)
            {
                // Exception text can contain entry content or SQL; the type is enough, and the next poll tries this feed again.
                database.ChangeTracker.Clear();
                failures.Add($"{feed.CatalogId} storage");
                logger.LogWarning("News feed {FeedId} could not be stored ({FailureType}).", feed.CatalogId, exception.GetType().FullName);
            }
        }
        var cutoff = now - Retention;
        var removed = await database.NewsFeedEntries.Where(entry => entry.FirstSeenUtc < cutoff).ExecuteDeleteAsync(cancellationToken);
        logger.LogInformation("News feeds polled: {Succeeded} of {Feeds} ok ({NotModified} unchanged), {Failed} failed, {NewEntries} new entries, "
            + "{Removed} expired entries removed. Failed feeds: {FailedFeeds}.", succeeded, feeds.Count, notModified, failures.Count, newEntries, removed,
            failures.Count == 0 ? "none" : string.Join(", ", failures.Take(MaxLoggedFailures)) + (failures.Count > MaxLoggedFailures ? ", …" : ""));
        return new(feeds.Count, succeeded, notModified, failures.Count, newEntries, removed);
    }

    private static string HostOf(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.IdnHost.ToLowerInvariant() : url;

    private async Task<NewsFeedFetchResult> FetchAsync(NewsFeedEntity feed, CancellationToken cancellationToken)
    {
        try
        {
            return await client.FetchAsync(feed.Url, feed.ETag, feed.LastModified, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The adapter returns every expected failure; anything else is a fault in one feed's handling and must not end the run.
            logger.LogWarning("News feed {FeedId} failed unexpectedly ({FailureType}).", feed.CatalogId, exception.GetType().FullName);
            return NewsFeedFetchResult.Failed("unexpected");
        }
    }

    private async Task<int> AddEntriesAsync(NewsFeedEntity feed, IReadOnlyList<NewsFeedDocumentEntry> entries, DateTime now,
        CancellationToken cancellationToken)
    {
        var cutoff = now - Retention;
        // An entry older than the retention would be deleted by this very run and come back as "new" on the next one.
        var candidates = entries.Select(entry => (Entry: entry, Canonical: CanonicalUrl.Normalize(entry.Url)!))
            .Where(pair => pair.Canonical is not null && (pair.Entry.PublishedUtc is null || pair.Entry.PublishedUtc >= cutoff))
            .DistinctBy(pair => pair.Canonical)
            .ToList();
        if (candidates.Count == 0) return 0;
        var urls = candidates.Select(pair => pair.Canonical).ToList();
        var known = (await database.NewsFeedEntries.Where(entry => urls.Contains(entry.CanonicalUrl)).Select(entry => entry.CanonicalUrl)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var (entry, canonical) in candidates.Where(pair => !known.Contains(pair.Canonical)))
        {
            // A feed with a wrong clock must not pin its entry to the top of a newest-first list for days.
            var published = entry.PublishedUtc > now ? now : entry.PublishedUtc;
            database.NewsFeedEntries.Add(new NewsFeedEntryEntity
            {
                FeedId = feed.Id, CanonicalUrl = canonical, Url = entry.Url, Title = entry.Title,
                PublishedUtc = published,
                Summary = entry.Summary, ImageUrl = entry.ImageUrl, FirstSeenUtc = now, SortUtc = published ?? now, CreatedUtc = now
            });
            added++;
        }
        await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();
        return added;
    }

    private Task<int> RecordAsync(NewsFeedEntity feed, NewsFeedFetchResult result, DateTime now, CancellationToken cancellationToken)
        => database.NewsFeeds.Where(row => row.Id == feed.Id).ExecuteUpdateAsync(update =>
        {
            update.SetProperty(row => row.LastAttemptUtc, (DateTime?)now);
            if (result.Status == NewsFeedFetchStatus.Failed)
            {
                update.SetProperty(row => row.ConsecutiveFailures, row => row.ConsecutiveFailures + 1);
                update.SetProperty(row => row.LastError, result.ErrorCode);
                return;
            }
            update.SetProperty(row => row.LastSuccessUtc, (DateTime?)now);
            update.SetProperty(row => row.ConsecutiveFailures, 0);
            update.SetProperty(row => row.LastError, (string?)null);
            // A 304 keeps the validators that earned it.
            if (result.Status == NewsFeedFetchStatus.Fetched)
            {
                update.SetProperty(row => row.ETag, result.ETag);
                update.SetProperty(row => row.LastModified, result.LastModified);
            }
        }, cancellationToken);
}
