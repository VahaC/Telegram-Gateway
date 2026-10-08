namespace TelegramGateway.TestInfrastructure.Time;

public sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset current = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => current;
    public void Advance(TimeSpan elapsed) => current += elapsed;
}
