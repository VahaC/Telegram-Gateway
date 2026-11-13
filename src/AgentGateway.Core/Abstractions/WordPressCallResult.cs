namespace AgentGateway.Core.Abstractions;

public sealed record WordPressCallResult<T>(WordPressCallOutcome Outcome, T? Value = default, string? ErrorCode = null)
{
    public bool Succeeded => Outcome == WordPressCallOutcome.Succeeded;
}
