using System.Globalization;
using AgentGateway.Api.Contracts.News;
using AgentGateway.Core.Entities;

namespace AgentGateway.Api.Services.News;

/// <summary>Builds the WordPress bodies from the stored document, never from the editable rows.</summary>
public static class NewsPayloadBuilder
{
    public static string DayBody(NewsIssueEntity issue, NewsIssueRequest document) => NewsJson.Serialize(new WordPressDayBody(
        document.Date, document.Title, document.Language, document.Intro, document.Releases ?? [], document.SecurityAlerts ?? [],
        document.Research,
        document.Items.OrderBy(item => item.Number)
            .Select(item => new WordPressItemOrderEntry(item.Number, item.EventKey, item.Category, item.IsTop)).ToList(),
        Stamp(issue)));

    public static string ItemBody(NewsIssueEntity issue, NewsItemRequest item) => NewsJson.Serialize(new WordPressItemBody(
        item.Number, item.Category, item.IsTop, item.Importance, item.Title, item.Summary, item.WhyItMatters, item.PublishedOn,
        item.EventOn, item.ReleaseStatus, item.Version, item.EventKey, item.Source, item.ExtraSources ?? [], item.Image,
        item.Tags ?? [], issue.Date, Stamp(issue)));

    /// <summary>The contract has every call carry the gateway stamp; a DELETE sends it as its only body field.</summary>
    public static string RemovalBody(NewsIssueEntity issue) => NewsJson.Serialize(new WordPressRemovalBody(Stamp(issue)));

    private static WordPressGatewayStamp Stamp(NewsIssueEntity issue) => new(issue.Id, issue.Revision,
        DateTime.SpecifyKind(issue.SubmittedUtc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
}
