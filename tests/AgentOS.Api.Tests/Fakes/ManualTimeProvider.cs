namespace AgentOS.Api.Tests.Fakes;

/// <summary>Deterministic <see cref="TimeProvider"/> for sampling-window and timestamp tests.</summary>
public sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; private set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan delta) => UtcNow += delta;
}
