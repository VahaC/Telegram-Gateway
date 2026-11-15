namespace AgentGateway.TestInfrastructure.Time;

public sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset current;

    public MutableTimeProvider() : this(DateTimeOffset.UtcNow) { }

    /// <summary>A fixed start for schedule tests; the API key path does not validate tokens against the system clock.</summary>
    public MutableTimeProvider(DateTimeOffset start) => current = start;

    public override DateTimeOffset GetUtcNow() => current;
    public void Advance(TimeSpan elapsed) => current += elapsed;
}
