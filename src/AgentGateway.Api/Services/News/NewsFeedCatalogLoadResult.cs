namespace AgentGateway.Api.Services.News;

/// <summary>
/// Readable is false when the file itself is missing or malformed (Errors holds the one reason). Otherwise Feeds are the
/// valid feeds and Errors name every feed that was skipped and why.
/// </summary>
public sealed record NewsFeedCatalogLoadResult(bool Readable, IReadOnlyList<NewsFeedCatalogFeed> Feeds, IReadOnlyList<string> Errors);
