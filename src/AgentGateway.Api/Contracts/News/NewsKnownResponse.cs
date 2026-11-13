namespace AgentGateway.Api.Contracts.News;

/// <summary>A candidate already in the registry. Kind is url, extraSource, eventKey, release or securityAlert.</summary>
public sealed record NewsKnownResponse(string Value, string Kind, DateOnly IssueDate, int? ItemNumber, string Title);
