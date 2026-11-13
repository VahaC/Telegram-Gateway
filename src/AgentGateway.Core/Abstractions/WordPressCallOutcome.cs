namespace AgentGateway.Core.Abstractions;

public enum WordPressCallOutcome
{
    Succeeded = 0,
    Retryable = 1,
    PermanentFailure = 2
}
