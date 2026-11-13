namespace AgentGateway.Api.Contracts.News;

/// <summary>Carried by every call; WordPress replaces content only for a greater revision.</summary>
public sealed record WordPressGatewayStamp(Guid IssueId, int Revision, string SubmittedAt);
