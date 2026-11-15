namespace AgentGateway.Api.Contracts.News;

/// <summary>
/// A lead from a publisher's own feed. FeedId is the catalog id; PublishedUtc is the feed's date (null when it gave none,
/// then FirstSeenUtc is when the gateway first saw the entry). Summary is plain text: cut to about 200 characters in a
/// list, whole (up to 4000) for a single entry. ImageUrl is an https picture named by the feed.
/// </summary>
public sealed record NewsFeedEntryResponse(
    Guid Id, string FeedId, string FeedName, IReadOnlyList<string> FeedCategories, string Kind, string Title, string Url,
    DateTime? PublishedUtc, DateTime FirstSeenUtc, string Summary, string? ImageUrl);
