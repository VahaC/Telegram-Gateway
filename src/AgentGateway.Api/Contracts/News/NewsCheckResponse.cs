namespace AgentGateway.Api.Contracts.News;

public sealed record NewsCheckResponse(IReadOnlyList<NewsKnownResponse> Known);
