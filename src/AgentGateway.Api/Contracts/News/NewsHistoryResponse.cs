namespace AgentGateway.Api.Contracts.News;

public sealed record NewsHistoryResponse(int Days, IReadOnlyList<NewsHistoryItemResponse> Items,
    IReadOnlyList<NewsHistoryEntryResponse> ReleasesAndAlerts);
