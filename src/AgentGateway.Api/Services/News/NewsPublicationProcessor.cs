using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AgentGateway.Api.Contracts;
using AgentGateway.Api.Contracts.News;
using AgentGateway.Api.Services.Delivery;
using AgentGateway.Core.Abstractions;
using AgentGateway.Core.Entities;
using AgentGateway.Core.Enums;
using AgentGateway.Core.Options;
using AgentGateway.Infrastructure.Persistence;

namespace AgentGateway.Api.Services.News;

/// <summary>
/// Publishes one queued issue to WordPress, one call at a time: the day, every unfinished item, removals of dropped items,
/// then a verification read. Unlike Telegram (ADR 0001), every write is an idempotent upsert, so a lost answer is retried (ADR 0003).
/// </summary>
public sealed class NewsPublicationProcessor(ApplicationDbContext database, IWordPressNewsClient wordPress, DeliveryService deliveries,
    IOptions<NewsOptions> options, TimeProvider time, ILogger<NewsPublicationProcessor> logger)
{
    private static readonly HashSet<string> AuthenticationFailures = ["wp_401", "wp_403"];

    public async Task<bool> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) return false;
        var identifier = await database.NewsIssues.Where(row => row.Status == NewsPublicationStatus.Pending)
            .OrderBy(row => row.UpdatedUtc).Select(row => (Guid?)row.Id).FirstOrDefaultAsync(cancellationToken);
        if (identifier is null) return false;
        var claimed = await database.NewsIssues.Where(row => row.Id == identifier && row.Status == NewsPublicationStatus.Pending)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, NewsPublicationStatus.Publishing)
                .SetProperty(row => row.Attempts, row => row.Attempts + 1)
                .SetProperty(row => row.UpdatedUtc, time.GetUtcNow().UtcDateTime), cancellationToken);
        if (claimed == 0) return false;
        var issue = await database.NewsIssues.AsSplitQuery().Include(row => row.Items).Include(row => row.Removals)
            .SingleAsync(row => row.Id == identifier, cancellationToken);
        if (issue.DeleteRequestedUtc is not null)
        {
            await DeleteFixtureAsync(issue, cancellationToken);
            return true;
        }
        var document = NewsJson.Deserialize<NewsIssueRequest>(issue.Document);
        var stopCode = await PublishAsync(issue, document, cancellationToken);
        await FinishAsync(issue, stopCode, cancellationToken);
        return true;
    }

    /// <summary>Trashes the fixture's item posts; once WordPress confirmed every removal, the issue row goes too.</summary>
    private async Task DeleteFixtureAsync(NewsIssueEntity issue, CancellationToken cancellationToken)
    {
        var stopCode = await RemoveAsync(issue, cancellationToken);
        var failed = issue.Removals.FirstOrDefault(removal => removal.Status == NewsItemStatus.Failed
            && database.Entry(removal).State != EntityState.Detached);
        if (stopCode is null && failed is null)
        {
            database.NewsIssues.Remove(issue);
            await SaveAsync();
            logger.LogInformation("News fixture issue {IssueId} was deleted.", issue.Id);
            return;
        }
        issue.Status = NewsPublicationStatus.Failed;
        issue.ErrorCode = stopCode ?? failed?.ErrorCode;
        issue.UpdatedUtc = time.GetUtcNow().UtcDateTime;
        await SaveAsync();
    }

    /// <summary>Runs the remaining calls; returns an error code when the issue cannot continue at all.</summary>
    private async Task<string?> PublishAsync(NewsIssueEntity issue, NewsIssueRequest document, CancellationToken cancellationToken)
    {
        if (issue.DayRevision != issue.Revision)
        {
            var day = await CallAsync(token => wordPress.PutDayAsync(issue.Date, NewsPayloadBuilder.DayBody(issue, document), token),
                () => { }, cancellationToken);
            if (!day.Succeeded) return day.ErrorCode;
            issue.DayPostId = day.Value!.PostId;
            issue.DayUrl = day.Value.Url;
            issue.DayRevision = issue.Revision;
            await SaveAsync();
            await PauseAsync(options.Value.CallIntervalMilliseconds, cancellationToken);
        }
        var bodies = document.Items.ToDictionary(item => item.EventKey);
        foreach (var item in issue.Items.Where(row => row.Status == NewsItemStatus.Pending).OrderBy(row => row.Number))
        {
            var result = await CallAsync(token => wordPress.PutItemAsync(item.EventKey, NewsPayloadBuilder.ItemBody(issue, bodies[item.EventKey]), token),
                () => item.Attempts++, cancellationToken);
            if (result.Succeeded)
            {
                item.Status = NewsItemStatus.Published;
                item.PublishedRevision = issue.Revision;
                item.WpPostId = result.Value!.PostId;
                item.WpUrl = result.Value.Url;
                // A picture WordPress could not fetch is recorded, but the news itself is on the site.
                item.ImageResult = result.Value.Image ?? (item.ImageUrl is null ? "none" : null);
                item.ImageError = result.Value.ImageError;
                item.ErrorCode = null;
            }
            else
            {
                item.Status = NewsItemStatus.Failed;
                item.ErrorCode = result.ErrorCode;
            }
            await SaveAsync();
            // Every later call would get the same refusal; stop instead of spending the whole retry budget per item.
            if (AuthenticationFailures.Contains(result.ErrorCode ?? "")) return result.ErrorCode;
            await PauseAsync(options.Value.CallIntervalMilliseconds, cancellationToken);
        }
        if (await RemoveAsync(issue, cancellationToken) is { } removalStop) return removalStop;
        var verify = await CallAsync(token => wordPress.GetDayAsync(issue.Date, token), () => { }, cancellationToken);
        if (!verify.Succeeded) return verify.ErrorCode;
        return Verify(issue, verify.Value!);
    }

    private async Task<string?> RemoveAsync(NewsIssueEntity issue, CancellationToken cancellationToken)
    {
        foreach (var removal in issue.Removals.Where(row => row.Status == NewsItemStatus.Pending).ToList())
        {
            var result = await CallAsync(token => wordPress.DeleteItemAsync(removal.EventKey, NewsPayloadBuilder.RemovalBody(issue), token), () => removal.Attempts++, cancellationToken);
            if (result.Succeeded) database.NewsRemovals.Remove(removal);
            else
            {
                removal.Status = NewsItemStatus.Failed;
                removal.ErrorCode = result.ErrorCode;
            }
            await SaveAsync();
            if (AuthenticationFailures.Contains(result.ErrorCode ?? "")) return result.ErrorCode;
            await PauseAsync(options.Value.CallIntervalMilliseconds, cancellationToken);
        }
        return null;
    }

    /// <summary>Compares what WordPress reports for the day with what was sent; items it reports missing are not on the site.</summary>
    private static string? Verify(NewsIssueEntity issue, WordPressDayStatus day)
    {
        var missing = day.Missing.ToHashSet();
        var reportedMissing = issue.Items.Where(item => item.Status == NewsItemStatus.Published && missing.Contains(item.EventKey)).ToList();
        foreach (var item in reportedMissing)
        {
            item.Status = NewsItemStatus.Failed;
            item.ErrorCode = "wp_missing";
        }
        var failed = issue.Items.Where(item => item.Status == NewsItemStatus.Failed).Select(item => item.EventKey).ToHashSet();
        var published = issue.Items.Count(item => item.Status == NewsItemStatus.Published);
        var consistent = day.ExpectedItems == issue.Items.Count && day.PublishedItems == published && missing.SetEquals(failed);
        return consistent ? null : "verify_mismatch";
    }

    private async Task FinishAsync(NewsIssueEntity issue, string? stopCode, CancellationToken cancellationToken)
    {
        var published = issue.Items.Count(item => item.Status == NewsItemStatus.Published);
        var failedItem = issue.Items.Where(item => item.Status == NewsItemStatus.Failed).OrderBy(item => item.Number).FirstOrDefault();
        var failedRemoval = issue.Removals.FirstOrDefault(removal => removal.Status == NewsItemStatus.Failed
            && database.Entry(removal).State != EntityState.Detached);
        var verified = stopCode is null;
        issue.Status = verified && failedItem is null && failedRemoval is null ? NewsPublicationStatus.Published
            : published > 0 && issue.DayRevision == issue.Revision && stopCode is null or "verify_mismatch" ? NewsPublicationStatus.PartiallyPublished
            : NewsPublicationStatus.Failed;
        issue.ErrorCode = stopCode ?? failedItem?.ErrorCode ?? failedRemoval?.ErrorCode;
        issue.UpdatedUtc = time.GetUtcNow().UtcDateTime;
        if (issue.Status == NewsPublicationStatus.Published) issue.PublishedUtc = issue.UpdatedUtc;
        await SaveAsync();
        logger.LogInformation("News issue {IssueId} revision {Revision} finished as {Status} with {Published} of {Total} items ({ErrorCode}).",
            issue.Id, issue.Revision, issue.Status, published, issue.Items.Count, issue.ErrorCode);
        await NotifyAsync(issue, published, cancellationToken);
    }

    /// <summary>Queues the single Telegram line once per revision; the idempotency key makes a repeat a no-op even after a crash.</summary>
    private async Task NotifyAsync(NewsIssueEntity issue, int published, CancellationToken cancellationToken)
    {
        if (!options.Value.NotifyTelegramOnPublish || issue.NoticeRevision == issue.Revision || issue.DayUrl is null
            || issue.Status is not (NewsPublicationStatus.Published or NewsPublicationStatus.PartiallyPublished))
            return;
        var total = issue.Items.Count;
        var count = published == total ? $"{total} {NewsWord(total)}" : $"{published} з {total} {NewsWord(total)}";
        var text = $"📰 {issue.Title} — {count}: {issue.DayUrl}";
        var key = $"news-published-{issue.Date:yyyy-MM-dd}-r{issue.Revision}";
        var result = await deliveries.SubmitMessageAsync(new MessageRequest(text, "plain", false, key), cancellationToken);
        if (result.Delivery is { } delivery)
        {
            issue.NoticeDeliveryId = delivery.Id;
            issue.NoticeRevision = issue.Revision;
            await SaveAsync();
        }
        else logger.LogWarning("News issue {IssueId} notice was not queued ({Status}).", issue.Id, result.Status);
    }

    private async Task<WordPressCallResult<T>> CallAsync<T>(Func<CancellationToken, Task<WordPressCallResult<T>>> call, Action onAttempt,
        CancellationToken cancellationToken)
    {
        for (var index = 0; ; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onAttempt();
            var result = await call(cancellationToken);
            if (result.Outcome != WordPressCallOutcome.Retryable || index + 1 >= options.Value.MaxAttempts) return result;
            await PauseAsync(options.Value.RetryBaseMilliseconds * (1 << index), cancellationToken);
        }
    }

    // Recording an observed outcome must not be skipped because the host is stopping.
    private Task SaveAsync() => database.SaveChangesAsync(CancellationToken.None);

    private Task PauseAsync(int milliseconds, CancellationToken cancellationToken) => milliseconds == 0
        ? Task.CompletedTask : Task.Delay(TimeSpan.FromMilliseconds(milliseconds), time, cancellationToken);

    /// <summary>Ukrainian plural of "новина" for a count.</summary>
    public static string NewsWord(int count) => (count % 100) is >= 11 and <= 14 ? "новин"
        : (count % 10) switch { 1 => "новина", 2 or 3 or 4 => "новини", _ => "новин" };
}
