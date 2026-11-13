using AgentGateway.Core.Enums;

namespace AgentGateway.Api.Contracts.News;

public sealed record NewsItemStatusResponse(int Number, string EventKey, string Category, bool IsTop, NewsItemStatus Status,
    int? PublishedRevision, string? Url, string? Image, string? ImageError, int Attempts, string? ErrorCode);
