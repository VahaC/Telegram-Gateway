namespace AgentGateway.TestInfrastructure.Http;

public sealed record FeedRequestCapture(string Url, string UserAgent, string? IfNoneMatch, string? IfModifiedSince);
