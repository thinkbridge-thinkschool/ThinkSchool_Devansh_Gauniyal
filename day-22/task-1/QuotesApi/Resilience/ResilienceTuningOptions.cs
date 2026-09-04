namespace QuotesApi.Resilience;

// Bound from the "ResilienceTuning" configuration section - the same pattern Day 21's
// MeasurementOptions already established for ArtificialDbDelayMs. Every value here has
// a production default (explained in README.md), but tests override them to much
// shorter windows so the breaker lifecycle (closed -> open -> half-open -> closed) is
// observable in well under a second instead of the 5-15 real seconds production
// tuning would take - see CachingTests.cs and Day 21's precedent for the same
// "override via config, not by editing the pipeline code" approach.
public sealed class ResilienceTuningOptions
{
    public const string SectionName = "ResilienceTuning";

    public double FailureRatio { get; set; } = 0.5;
    public int SamplingDurationMs { get; set; } = 5000;
    public int MinimumThroughput { get; set; } = 4;
    public int BreakDurationMs { get; set; } = 10000;
    public int TimeoutMs { get; set; } = 2000;
    public int BulkheadPermitLimit { get; set; } = 8;
    public int BulkheadQueueLimit { get; set; } = 4;
    public int RetryMaxAttempts { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 1000;
    public bool RetryUseJitter { get; set; } = true;

    public TimeSpan SamplingDuration => TimeSpan.FromMilliseconds(SamplingDurationMs);
    public TimeSpan BreakDuration => TimeSpan.FromMilliseconds(BreakDurationMs);
    public TimeSpan Timeout => TimeSpan.FromMilliseconds(TimeoutMs);
    public TimeSpan RetryBaseDelay => TimeSpan.FromMilliseconds(RetryBaseDelayMs);
}
