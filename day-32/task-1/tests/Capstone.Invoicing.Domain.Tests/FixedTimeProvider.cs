namespace Capstone.Invoicing.Domain.Tests;

// A settable TimeProvider for deterministic tests - Invoice never calls
// DateTimeOffset.UtcNow directly, precisely so tests can control "now" instead of
// racing the clock (relevant for the deemed-approval SLA tests in particular).
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
