using AgentGateway.Api.Contracts;
using AgentGateway.Api.Contracts.News;
using AgentGateway.Core.Entities;
using AgentGateway.Core.Enums;
using AgentGateway.Core.News;

namespace AgentGateway.Api.Mapping;

public sealed class NewsMapper : INewsMapper
{
    public void ApplyContent(NewsIssueEntity issue, NewsIssueRequest request, DateTime nowUtc)
    {
        issue.Date = request.Date;
        issue.Title = request.Title;
        issue.Language = request.Language;
        issue.Intro = request.Intro;
        issue.ResearchMode = request.Research?.Mode;
        issue.ResearchSearchPasses = request.Research?.SearchPasses;
        issue.ResearchCandidates = request.Research?.Candidates;
        issue.ResearchDuplicatesRemoved = request.Research?.DuplicatesRemoved;
        issue.ResearchWindowHours = request.Research?.WindowHours;
        issue.Items = request.Items.Select(item => new NewsItemEntity
        {
            CreatedUtc = nowUtc, Number = item.Number, Category = item.Category, IsTop = item.IsTop, Importance = item.Importance,
            Title = item.Title, Summary = item.Summary, WhyItMatters = item.WhyItMatters, PublishedOn = item.PublishedOn,
            EventOn = item.EventOn, ReleaseStatus = item.ReleaseStatus, Version = item.Version, EventKey = item.EventKey,
            SourceUrl = item.Source.Url, SourceName = item.Source.Name, CanonicalUrl = CanonicalUrl.Normalize(item.Source.Url)!,
            ImageUrl = item.Image?.Url, ImageAlt = item.Image?.Alt, Tags = item.Tags?.ToList() ?? [],
            Status = NewsItemStatus.Pending,
            ExtraSources = (item.ExtraSources ?? []).Select((source, position) => new NewsSourceEntity
            {
                CreatedUtc = nowUtc, Position = position, Url = source.Url, Name = source.Name,
                CanonicalUrl = CanonicalUrl.Normalize(source.Url)!
            }).ToList()
        }).ToList();
        issue.Releases = (request.Releases ?? []).Select((release, position) => new NewsReleaseEntity
        {
            CreatedUtc = nowUtc, Position = position, Name = release.Name, Version = release.Version, Date = release.Date,
            Url = release.Url, CanonicalUrl = CanonicalUrl.Normalize(release.Url)!, Note = release.Note
        }).ToList();
        issue.SecurityAlerts = (request.SecurityAlerts ?? []).Select((alert, position) => new NewsSecurityAlertEntity
        {
            CreatedUtc = nowUtc, Position = position, AlertId = alert.Id, Product = alert.Product, Severity = alert.Severity,
            Cvss = alert.Cvss, Date = alert.Date, Url = alert.Url, CanonicalUrl = CanonicalUrl.Normalize(alert.Url)!, Note = alert.Note
        }).ToList();
    }

    public NewsIssueStatusResponse ToResponse(NewsIssueEntity issue, DeliveryResponse? notice)
    {
        var items = issue.Items.OrderBy(item => item.Number).ToList();
        return new(issue.Id, issue.Date, issue.Title, issue.Revision, issue.Status, issue.Attempts, issue.ErrorCode,
            issue.DayUrl, issue.DayPostId, issue.SubmittedUtc, issue.UpdatedUtc, issue.PublishedUtc,
            items.Count, items.Count(item => item.Status == NewsItemStatus.Published), items.Count(item => item.Status == NewsItemStatus.Failed),
            NewsCategories.All.ToDictionary(category => category, category => items.Count(item => item.Category == category)),
            issue.Releases.Count, issue.SecurityAlerts.Count,
            items.Select(item => new NewsItemStatusResponse(item.Number, item.EventKey, item.Category, item.IsTop, item.Status,
                item.PublishedRevision, item.WpUrl, item.ImageResult, item.ImageError, item.Attempts, item.ErrorCode)).ToList(),
            issue.Removals.OrderBy(removal => removal.EventKey)
                .Select(removal => new NewsRemovalStatusResponse(removal.EventKey, removal.Status, removal.Attempts, removal.ErrorCode)).ToList(),
            notice is null ? null : new NewsNoticeResponse(notice.Id, issue.NoticeRevision ?? issue.Revision, notice.Status, notice.RequiresReview));
    }
}
