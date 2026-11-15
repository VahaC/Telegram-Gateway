namespace AgentGateway.Core.News;

/// <summary>What kind of publisher a catalog feed is; the agent weighs a vendor's own post differently from an aggregator's.</summary>
public static class NewsFeedKinds
{
    public static readonly string[] All = ["vendor", "release", "news", "blog", "advisory", "community", "aggregator"];
}
