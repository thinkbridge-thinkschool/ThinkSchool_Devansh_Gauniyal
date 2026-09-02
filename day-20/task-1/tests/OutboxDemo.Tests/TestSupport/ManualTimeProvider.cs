namespace OutboxDemo.Tests.TestSupport;

/// <summary>
/// Deterministic clock so lease-expiry can be exercised without Thread.Sleep
/// or wall-clock waits — tests advance it explicitly.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset start)
    {
        _now = start;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
