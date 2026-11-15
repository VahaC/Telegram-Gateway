namespace AgentGateway.Api.Services.News;

/// <summary>Outcome of a feed read; the HTTP routes and the MCP tools translate the status each in their own way.</summary>
public sealed record NewsFeedQueryResult<T>(NewsFeedQueryStatus Status, T? Value = null, string? Error = null) where T : class;
