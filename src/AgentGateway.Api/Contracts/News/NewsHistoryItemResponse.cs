namespace AgentGateway.Api.Contracts.News;

public sealed record NewsHistoryItemResponse(DateOnly Date, int Number, string Category, string Title, string EventKey, string CanonicalUrl);
