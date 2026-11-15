namespace AgentGateway.Api.Services.News;

/// <summary>The root of <c>news-feeds.json</c>: <c>{ "version": 1, "feeds": [...] }</c>.</summary>
public sealed record NewsFeedCatalogDocument(int Version, IReadOnlyList<NewsFeedCatalogFeed?>? Feeds);
