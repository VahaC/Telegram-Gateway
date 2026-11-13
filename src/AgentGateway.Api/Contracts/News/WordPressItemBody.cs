namespace AgentGateway.Api.Contracts.News;

/// <summary>Body of PUT /items/{eventKey}: one NewsItem plus issueDate and gateway.</summary>
public sealed record WordPressItemBody(
    int Number, string Category, bool IsTop, int Importance, string Title, string Summary, string WhyItMatters,
    DateOnly PublishedOn, DateOnly? EventOn, string? ReleaseStatus, string? Version, string EventKey,
    NewsSourceRequest Source, IReadOnlyList<NewsSourceRequest> ExtraSources, NewsImageRequest? Image,
    IReadOnlyList<string> Tags, DateOnly IssueDate, WordPressGatewayStamp Gateway);
