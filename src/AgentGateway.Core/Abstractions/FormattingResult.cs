namespace AgentGateway.Core.Abstractions;

public sealed record FormattingResult(IReadOnlyList<string> Parts, string? Error = null)
{
    public bool IsSuccess => Error is null;
}
