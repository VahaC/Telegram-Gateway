namespace AgentGateway.Core.Abstractions;

public sealed record NewsFeedFetchResult(
    NewsFeedFetchStatus Status,
    IReadOnlyList<NewsFeedDocumentEntry> Entries,
    string? ETag = null,
    string? LastModified = null,
    string? ErrorCode = null)
{
    public static NewsFeedFetchResult NotModified { get; } = new(NewsFeedFetchStatus.NotModified, []);

    public static NewsFeedFetchResult Failed(string errorCode) => new(NewsFeedFetchStatus.Failed, [], ErrorCode: errorCode);
}
