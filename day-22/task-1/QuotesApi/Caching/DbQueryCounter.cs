namespace QuotesApi.Caching;

// Registered as a singleton per app/test host (see Program.cs), not a static field:
// a static counter would leak between WebApplicationFactory instances under xUnit's
// default parallel test execution and make the stampede-count assertions flaky.
public sealed class DbQueryCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Increment() => Interlocked.Increment(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);
}
