namespace AgentGateway.Core.Abstractions;

/// <summary>
/// Adapter for publishers' RSS/Atom feeds. It behaves like an ordinary feed reader: one plain GET with the gateway's own
/// User-Agent and the validators of the last answer. A refusal (403, 402, 429 …) is returned as a failure, never worked around.
/// Returns results for every expected failure; it does not throw for them.
/// </summary>
public interface INewsFeedClient
{
    Task<NewsFeedFetchResult> FetchAsync(string url, string? eTag, string? lastModified, CancellationToken cancellationToken);
}
