namespace AgentGateway.Api.Services.News;

/// <summary>One feed of <c>news-feeds.json</c>. Every field is required; the loader checks each one.</summary>
public sealed record NewsFeedCatalogFeed(
    string? Id, string? Name, string? Url, IReadOnlyList<string>? Categories, string? Kind, string? Language);
