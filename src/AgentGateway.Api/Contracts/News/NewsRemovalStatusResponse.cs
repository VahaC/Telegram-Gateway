using AgentGateway.Core.Enums;

namespace AgentGateway.Api.Contracts.News;

public sealed record NewsRemovalStatusResponse(string EventKey, NewsItemStatus Status, int Attempts, string? ErrorCode);
