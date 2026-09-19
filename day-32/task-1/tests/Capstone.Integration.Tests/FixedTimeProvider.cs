namespace Capstone.Integration.Tests;

// Same shape as tests/Capstone.Invoicing.Domain.Tests/FixedTimeProvider.cs - not
// shared via a project reference because these two test projects are otherwise
// independent (one exercises the domain in isolation, this one exercises real
// persistence) and this is five lines.
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
