// Parses the raw output/*.txt bombardier reports and output/*-db-queries.json counter
// snapshots captured by run-measurement.sh into output/summary.md: DB queries/sec,
// cache hit rate, and latency percentiles for both the uncached and cached paths.
//
// The percentile parsing (ParsePercentileMilliseconds below) is copied verbatim from
// day-11/task-1/QuotesApi.Performance.Tests/LatencyPercentileParser.cs (also present at
// day-11/task-2/FastApi.Tests/LatencyPercentileParser.cs) - see PROVENANCE.md. Numbers
// are read out of bombardier's own computed report, never estimated or rounded here.
//
// Run via: dotnet run scripts/parse-measurement.cs -- <output-dir>
// (a .NET 10 SDK single-file program - no .csproj needed for this one-off tool.)

using System.Text.Json;
using System.Text.RegularExpressions;

var outputDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "output");

static double? ParsePercentileMilliseconds(string text, string percentileLabel)
{
    var pattern = $@"(?m)^\s*{Regex.Escape(percentileLabel)}\s+([\d.]+)\s*(ms|µs|us|s)\b";
    var match = Regex.Match(text, pattern);
    if (!match.Success)
    {
        return null;
    }

    var value = double.Parse(match.Groups[1].Value);
    var unit = match.Groups[2].Value;

    return unit switch
    {
        "s" => value * 1000.0,
        "ms" => value,
        "µs" or "us" => value / 1000.0,
        _ => value
    };
}

static int ParseTotalRequests(string bombardierText)
{
    // "    1xx - 0, 2xx - 133, 3xx - 0, 4xx - 0, 5xx - 0" - sum every code class,
    // since a run could in principle include non-2xx responses.
    var match = Regex.Match(bombardierText, @"1xx - (\d+), 2xx - (\d+), 3xx - (\d+), 4xx - (\d+), 5xx - (\d+)");
    if (!match.Success)
    {
        throw new InvalidOperationException("Could not parse HTTP code totals from bombardier output.");
    }

    return Enumerable.Range(1, 5).Sum(i => int.Parse(match.Groups[i].Value));
}

static int ReadCount(string path) =>
    JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("count").GetInt32();

static double ParseDurationSeconds(string duration)
{
    duration = duration.Trim();
    if (duration.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
    {
        return double.Parse(duration[..^2]) / 1000.0;
    }
    if (duration.EndsWith('s'))
    {
        return double.Parse(duration[..^1]);
    }
    if (duration.EndsWith('m'))
    {
        return double.Parse(duration[..^1]) * 60.0;
    }
    return double.Parse(duration);
}

var uncachedText = File.ReadAllText(Path.Combine(outputDir, "uncached-bombardier.txt"));
var cachedText = File.ReadAllText(Path.Combine(outputDir, "cached-bombardier.txt"));
var uncachedDbQueries = ReadCount(Path.Combine(outputDir, "uncached-db-queries.json"));
var cachedDbQueries = ReadCount(Path.Combine(outputDir, "cached-db-queries.json"));

using var paramsDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDir, "params.json")));
var concurrency = paramsDoc.RootElement.GetProperty("concurrency").GetInt32();
var durationText = paramsDoc.RootElement.GetProperty("duration").GetString()!;
var durationSeconds = ParseDurationSeconds(durationText);

var uncachedTotalRequests = ParseTotalRequests(uncachedText);
var cachedTotalRequests = ParseTotalRequests(cachedText);

// AuthorQuoteSummaryQuery is 1 authors query + 1 explicit Collection().Load() per
// author (50 seeded authors) = 51 real SQL round trips per factory run.
const int queriesPerFactoryRun = 51;
var uncachedFactoryRuns = uncachedDbQueries / (double)queriesPerFactoryRun;
var cachedFactoryRuns = cachedDbQueries / (double)queriesPerFactoryRun;

var uncachedDbQueriesPerSec = uncachedDbQueries / durationSeconds;
var cachedDbQueriesPerSec = cachedDbQueries / durationSeconds;

var cacheHitRate = cachedTotalRequests > 0
    ? (cachedTotalRequests - cachedFactoryRuns) / cachedTotalRequests
    : 0.0;

string Percentiles(string text) => string.Join(" / ", new[] { "50%", "90%", "99%" }
    .Select(label => $"{label}={ParsePercentileMilliseconds(text, label):F2}ms"));

var summary = $"""
    # Day 21 Task 1 — measurement summary

    Generated {DateTime.UtcNow:u} by `scripts/parse-measurement.cs` from the raw
    bombardier reports and DB-query-counter snapshots in this directory. Percentiles are
    read directly out of bombardier's own report using the parsing pattern copied from
    `day-11/task-1/QuotesApi.Performance.Tests/LatencyPercentileParser.cs` — not
    estimated or rounded by hand.

    **Parameters:** concurrency={concurrency}, duration={durationText} ({durationSeconds}s)

    | Path | Total requests | Real DB queries | Factory runs | DB queries/sec | Latency p50/p90/p99 |
    |---|---:|---:|---:|---:|---|
    | Uncached | {uncachedTotalRequests} | {uncachedDbQueries} | {uncachedFactoryRuns:F1} | {uncachedDbQueriesPerSec:F2} | {Percentiles(uncachedText)} |
    | Cached | {cachedTotalRequests} | {cachedDbQueries} | {cachedFactoryRuns:F1} | {cachedDbQueriesPerSec:F2} | {Percentiles(cachedText)} |

    **Cache hit rate (cached path, key starts cold - the first concurrent wave races and coalesces, then every later request is a real hit):**
    {cacheHitRate:P1} ({cachedTotalRequests - (int)cachedFactoryRuns} of {cachedTotalRequests} requests served without a DB round trip)

    **DB load drop:** {uncachedDbQueriesPerSec:F2} → {cachedDbQueriesPerSec:F2} DB queries/sec
    ({(uncachedDbQueriesPerSec == 0 ? "n/a" : $"{(1 - cachedDbQueriesPerSec / uncachedDbQueriesPerSec):P1} reduction")})

    These are local, single-machine numbers against SQLite with an artificial DB delay
    (`Measurement:ArtificialDbDelayMs`) — see README.md for what that means for how far
    they generalise.
    """;

File.WriteAllText(Path.Combine(outputDir, "summary.md"), summary);
Console.WriteLine(summary);
