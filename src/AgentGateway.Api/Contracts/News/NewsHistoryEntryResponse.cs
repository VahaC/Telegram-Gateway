namespace AgentGateway.Api.Contracts.News;

/// <summary>A release (Kind release, Name is the product) or a security alert (Kind securityAlert, Name is its id).</summary>
public sealed record NewsHistoryEntryResponse(DateOnly Date, string Kind, string Name, string CanonicalUrl);
