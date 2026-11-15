using Microsoft.EntityFrameworkCore;
using AgentGateway.Api.Contracts.News;
using AgentGateway.Core.News;
using AgentGateway.Infrastructure.Persistence;

namespace AgentGateway.Api.Services.News;

/// <summary>
/// The dedup registry check (409): every item ever accepted, in any other issue, plus repeats inside the submitted issue.
/// Releases and alerts are compared only with earlier releases and alerts.
/// </summary>
public sealed class NewsDuplicateFinder(ApplicationDbContext database)
{
    public async Task<IReadOnlyList<NewsDuplicateResponse>> FindAsync(NewsIssueRequest issue, Guid? ownIssueId, CancellationToken cancellationToken)
    {
        var own = ownIssueId ?? Guid.Empty;
        var duplicates = new List<NewsDuplicateResponse>();
        var primary = issue.Items.Select(item => CanonicalUrl.Normalize(item.Source.Url)!).ToList();
        var extras = issue.Items.SelectMany(item => item.ExtraSources ?? []).Select(source => CanonicalUrl.Normalize(source.Url)!).ToList();
        var keys = issue.Items.Select(item => item.EventKey).ToList();
        var lookupUrls = primary.Concat(extras).Distinct().ToList();
        var earlier = await database.NewsItems.AsNoTracking()
            .Where(item => item.IssueId != own && (lookupUrls.Contains(item.CanonicalUrl) || keys.Contains(item.EventKey)))
            .Join(database.NewsIssues, item => item.IssueId, owner => owner.Id,
                (item, owner) => new { item.CanonicalUrl, item.EventKey, item.Number, item.Title, owner.Date })
            .ToListAsync(cancellationToken);
        var byUrl = earlier.GroupBy(row => row.CanonicalUrl).ToDictionary(group => group.Key, group => group.First());
        var byKey = earlier.GroupBy(row => row.EventKey).ToDictionary(group => group.Key, group => group.First());
        var firstUrl = new Dictionary<string, NewsItemRequest>();
        var firstKey = new Dictionary<string, NewsItemRequest>();
        for (var index = 0; index < issue.Items.Count; index++)
        {
            var item = issue.Items[index];
            var url = primary[index];
            if (byUrl.TryGetValue(url, out var urlMatch))
                duplicates.Add(new($"items[{index}].source.url", url, null, urlMatch.Date, urlMatch.Number, urlMatch.Title));
            else if (!firstUrl.TryAdd(url, item))
                duplicates.Add(new($"items[{index}].source.url", url, null, issue.Date, firstUrl[url].Number, firstUrl[url].Title));
            if (byKey.TryGetValue(item.EventKey, out var keyMatch))
                duplicates.Add(new($"items[{index}].eventKey", null, item.EventKey, keyMatch.Date, keyMatch.Number, keyMatch.Title));
            else if (!firstKey.TryAdd(item.EventKey, item))
                duplicates.Add(new($"items[{index}].eventKey", null, item.EventKey, issue.Date, firstKey[item.EventKey].Number, firstKey[item.EventKey].Title));
        }
        for (var index = 0; index < issue.Items.Count; index++)
        {
            var sources = issue.Items[index].ExtraSources ?? [];
            for (var position = 0; position < sources.Count; position++)
            {
                var url = CanonicalUrl.Normalize(sources[position].Url)!;
                // A confirmation that is another item's primary material means both items describe one event.
                if (byUrl.TryGetValue(url, out var match))
                    duplicates.Add(new($"items[{index}].extraSources[{position}].url", url, null, match.Date, match.Number, match.Title));
                else if (firstUrl.TryGetValue(url, out var sibling) && sibling.Number != issue.Items[index].Number)
                    duplicates.Add(new($"items[{index}].extraSources[{position}].url", url, null, issue.Date, sibling.Number, sibling.Title));
            }
        }
        duplicates.AddRange(await FindReleasesAsync(issue, own, cancellationToken));
        duplicates.AddRange(await FindAlertsAsync(issue, own, cancellationToken));
        return duplicates;
    }

    /// <summary>
    /// Every canonical URL the registry knows: items' primary and extra sources, releases and alerts, the same set
    /// check_news_candidates reports as known. A query, so a caller can filter by it inside one SQL statement.
    /// </summary>
    public IQueryable<string> KnownUrls() => database.NewsItems.Select(item => item.CanonicalUrl)
        .Concat(database.NewsSources.Select(source => source.CanonicalUrl))
        .Concat(database.NewsReleases.Select(release => release.CanonicalUrl))
        .Concat(database.NewsSecurityAlerts.Select(alert => alert.CanonicalUrl));

    private async Task<IEnumerable<NewsDuplicateResponse>> FindReleasesAsync(NewsIssueRequest issue, Guid own, CancellationToken cancellationToken)
    {
        var releases = issue.Releases ?? [];
        var urls = releases.Select(release => CanonicalUrl.Normalize(release.Url)!).ToList();
        var earlier = await database.NewsReleases.AsNoTracking().Where(release => release.IssueId != own && urls.Contains(release.CanonicalUrl))
            .Join(database.NewsIssues, release => release.IssueId, owner => owner.Id, (release, owner) => new { release.CanonicalUrl, release.Name, owner.Date })
            .ToListAsync(cancellationToken);
        return Report(urls, "releases", issue.Date, index => releases[index].Name,
            earlier.ToDictionary(row => row.CanonicalUrl, row => (row.Date, row.Name)));
    }

    private async Task<IEnumerable<NewsDuplicateResponse>> FindAlertsAsync(NewsIssueRequest issue, Guid own, CancellationToken cancellationToken)
    {
        var alerts = issue.SecurityAlerts ?? [];
        var urls = alerts.Select(alert => CanonicalUrl.Normalize(alert.Url)!).ToList();
        var earlier = await database.NewsSecurityAlerts.AsNoTracking().Where(alert => alert.IssueId != own && urls.Contains(alert.CanonicalUrl))
            .Join(database.NewsIssues, alert => alert.IssueId, owner => owner.Id, (alert, owner) => new { alert.CanonicalUrl, alert.AlertId, owner.Date })
            .ToListAsync(cancellationToken);
        return Report(urls, "securityAlerts", issue.Date, index => alerts[index].Id,
            earlier.ToDictionary(row => row.CanonicalUrl, row => (row.Date, row.AlertId)));
    }

    private static List<NewsDuplicateResponse> Report(List<string> urls, string path, DateOnly date, Func<int, string> name,
        Dictionary<string, (DateOnly Date, string Name)> earlier)
    {
        var duplicates = new List<NewsDuplicateResponse>();
        var first = new Dictionary<string, int>();
        for (var index = 0; index < urls.Count; index++)
        {
            if (earlier.TryGetValue(urls[index], out var match))
                duplicates.Add(new($"{path}[{index}].url", urls[index], null, match.Date, null, match.Name));
            else if (!first.TryAdd(urls[index], index))
                duplicates.Add(new($"{path}[{index}].url", urls[index], null, date, null, name(first[urls[index]])));
        }
        return duplicates;
    }
}
