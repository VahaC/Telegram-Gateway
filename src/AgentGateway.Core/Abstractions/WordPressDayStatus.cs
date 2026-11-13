namespace AgentGateway.Core.Abstractions;

public sealed record WordPressDayStatus(long PostId, string Url, string Status, int Revision, int ExpectedItems,
    int PublishedItems, IReadOnlyList<string> Missing);
