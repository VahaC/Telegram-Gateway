namespace AgentGateway.TestInfrastructure.News;

/// <summary>One item of a fixture feed. A null Published leaves the date out of the feed.</summary>
public sealed record FeedItemFixture(string Title, string Url, DateTimeOffset? Published, string Description = "Text of the item.", string? ImageUrl = null);
