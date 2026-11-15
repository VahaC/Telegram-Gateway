namespace AgentGateway.Api.Services.News;

/// <summary>What one poll did. Succeeded includes NotModified (a 304 is a healthy feed with nothing new).</summary>
public sealed record NewsFeedPollSummary(int Feeds, int Succeeded, int NotModified, int Failed, int NewEntries, int RemovedEntries);
