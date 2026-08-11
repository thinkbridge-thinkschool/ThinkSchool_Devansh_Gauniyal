using QuotesApi.Services.Time;

namespace QuotesApi.Auth.Tests;

public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; private set; }

    public void Advance(TimeSpan duration)
    {
        UtcNow = UtcNow.Add(duration);
    }
}
