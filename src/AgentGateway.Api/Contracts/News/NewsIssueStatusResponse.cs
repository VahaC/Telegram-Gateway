using AgentGateway.Core.Enums;

namespace AgentGateway.Api.Contracts.News;

public sealed record NewsIssueStatusResponse(
    Guid Id, DateOnly Date, string Title, int Revision, NewsPublicationStatus Status, int Attempts, string? ErrorCode,
    string? DayUrl, long? DayPostId, DateTime SubmittedUtc, DateTime UpdatedUtc, DateTime? PublishedUtc,
    int ItemCount, int PublishedItemCount, int FailedItemCount, IReadOnlyDictionary<string, int> Categories,
    int ReleaseCount, int SecurityAlertCount, IReadOnlyList<NewsItemStatusResponse> Items,
    IReadOnlyList<NewsRemovalStatusResponse> Removals, NewsNoticeResponse? Notice);
