namespace QuotesApi.Resilience;

// Counts real invocations of the controllable external-service endpoint itself (not
// calls through the resilient client) - the concrete evidence that "an open breaker
// short-circuits without calling the dependency at all" and that retry really did
// attempt the configured number of times. Same Interlocked-counter shape as Day 21's
// DbQueryCounter, kept as a separate type rather than reused because it counts a
// different dependency and belongs to a different namespace's concerns.
public sealed class ExternalServiceCallCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Increment() => Interlocked.Increment(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);
}
