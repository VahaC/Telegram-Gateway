namespace AgentGateway.Api.Contracts.News;

/// <summary>One page of feed entries, newest first. Total counts every match; NextCursor is null on the last page.</summary>
public sealed record NewsFeedResponse(IReadOnlyList<NewsFeedEntryResponse> Entries, int Total, string? NextCursor, NewsFeedHealthResponse Health);
