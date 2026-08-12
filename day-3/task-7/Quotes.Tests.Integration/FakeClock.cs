using Quotes.Api.Time;

namespace Quotes.Tests.Integration;

public sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
