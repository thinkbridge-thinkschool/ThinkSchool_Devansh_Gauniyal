namespace QuotesApi.Caching;

// Bound from the "Measurement" configuration section (appsettings, environment
// variables, or user-secrets) - never hardcoded into the read path itself, so the
// artificial delay can be turned up for a stampede demo or down to zero without a
// code change.
public sealed class MeasurementOptions
{
    public const string SectionName = "Measurement";

    // How long a real DB read pauses before running the query, simulating a slow
    // query so a cache miss is expensive enough for a stampede to be observable.
    public int ArtificialDbDelayMs { get; set; } = 150;
}
