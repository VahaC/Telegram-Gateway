namespace AgentGateway.Core.Abstractions;

public enum NewsFeedFetchStatus
{
    /// <summary>The feed answered 200 and was parsed; entries and fresh validators are attached.</summary>
    Fetched = 0,
    /// <summary>The feed answered 304 to the conditional GET: nothing new since the last poll.</summary>
    NotModified = 1,
    /// <summary>Refused, unreachable, too large or not a feed; ErrorCode says which.</summary>
    Failed = 2
}
