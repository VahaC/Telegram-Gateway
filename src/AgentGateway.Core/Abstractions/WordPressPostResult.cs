namespace AgentGateway.Core.Abstractions;

public sealed record WordPressPostResult(long PostId, string Url, string Status, int Revision, string? Image, string? ImageError);
