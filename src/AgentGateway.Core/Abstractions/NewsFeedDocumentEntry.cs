namespace AgentGateway.Core.Abstractions;

/// <summary>One parsed feed item: plain-text title and summary, an absolute link, the feed's own date (if any) and an https image.</summary>
public sealed record NewsFeedDocumentEntry(string Title, string Url, DateTime? PublishedUtc, string Summary, string? ImageUrl);
