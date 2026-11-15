namespace AgentGateway.Api.Contracts.News;

/// <summary>Whether the collection behind a page can be trusted: enabled feeds, feeds whose last poll failed, newest successful poll.</summary>
public sealed record NewsFeedHealthResponse(int FeedsEnabled, int FeedsFailing, DateTime? LastSuccessfulPollUtc);
