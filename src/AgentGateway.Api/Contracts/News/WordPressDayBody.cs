namespace AgentGateway.Api.Contracts.News;

/// <summary>Body of PUT /days/{date}: the issue without item bodies; itemOrder replaces items.</summary>
public sealed record WordPressDayBody(
    DateOnly Date, string Title, string Language, string? Intro,
    IReadOnlyList<NewsReleaseRequest> Releases, IReadOnlyList<NewsSecurityAlertRequest> SecurityAlerts,
    NewsResearchRequest? Research, IReadOnlyList<WordPressItemOrderEntry> ItemOrder, WordPressGatewayStamp Gateway);
