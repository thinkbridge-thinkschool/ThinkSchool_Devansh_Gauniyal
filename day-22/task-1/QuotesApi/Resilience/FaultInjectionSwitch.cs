namespace QuotesApi.Resilience;

// TEST AND DEMO SCAFFOLDING ONLY - see FaultMode.cs. Registered as a keyed singleton
// per dependency ("redis", "external-service"; see DependencyKeys.cs) so Redis and
// the HTTP dependency can be put in different modes independently, from separate
// endpoints, without affecting each other. Mode is a plain int behind Volatile
// read/write rather than a lock: it's a single word, flipped by one endpoint call and
// read by many concurrent request-handling calls, so a lock would be pure overhead
// for a value that's either fully old or fully new on any given read - there's no
// multi-field invariant to protect.
public sealed class FaultInjectionSwitch(string dependencyName)
{
    private int _mode = (int)FaultMode.Healthy;

    public string DependencyName { get; } = dependencyName;

    public FaultMode Mode
    {
        get => (FaultMode)Volatile.Read(ref _mode);
        set => Volatile.Write(ref _mode, (int)value);
    }

    // How long a "Slow" call waits before proceeding - long enough that a resilience
    // pipeline's timeout (configured shorter than this) genuinely fires rather than
    // racing it.
    public TimeSpan SlowDelay { get; set; } = TimeSpan.FromSeconds(5);

    // Called from inside the real dependency call path (inside the resilience
    // pipeline's callback), so Failing/Slow modes are exactly what the pipeline's
    // strategies react to - a thrown exception here is what the circuit breaker
    // counts as a failure, and the delay here is what the timeout strategy times out.
    public async ValueTask MaybeInjectAsync(CancellationToken cancellationToken)
    {
        switch (Mode)
        {
            case FaultMode.Failing:
                throw new InjectedFaultException(DependencyName);
            case FaultMode.Slow:
                await Task.Delay(SlowDelay, cancellationToken);
                break;
            case FaultMode.Healthy:
            default:
                break;
        }
    }
}
