using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DapperComparison;

public static class Comparison
{
    public const int WarmupIterationsPerVariant = 1;
    public const int MeasuredIterationsPerVariant = 7;

    public static readonly DateTime SubmittedSinceUtc = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly string[] Variants = { "EfTracked", "EfProjection", "Dapper" };

    public static ComparisonResults Run(string dbPath)
    {
        var iterationRuns = Variants.ToDictionary(v => v, _ => new List<IterationRun>());
        var iterationOrders = new List<List<string>>();

        foreach (var variant in Variants)
        {
            ExecuteVariant(variant, dbPath);
        }

        for (int round = 0; round < MeasuredIterationsPerVariant; round++)
        {
            var order = Rotate(Variants, round % Variants.Length);
            iterationOrders.Add(order.ToList());

            foreach (var variant in order)
            {
                // Force a clean, comparable heap baseline before every measured iteration -
                // otherwise a collection pause landing inside one variant's window (and not
                // another's) would make the comparison about GC timing luck, not the variant.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var stopwatch = Stopwatch.StartNew();
                var rows = ExecuteVariant(variant, dbPath);
                stopwatch.Stop();
                long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

                iterationRuns[variant].Add(new IterationRun(
                    stopwatch.Elapsed.TotalMilliseconds,
                    allocatedAfter - allocatedBefore,
                    rows.Count));
            }
        }

        var variantResults = Variants.ToDictionary(v => v, v => Summarize(iterationRuns[v]));

        return new ComparisonResults(
            variantResults,
            iterationOrders,
            WarmupIterationsPerVariant,
            Environment.Version.ToString(),
            RuntimeInformation.RuntimeIdentifier,
            PackageVersion(typeof(Dapper.SqlMapper).Assembly),
            PackageVersion(typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly),
            "Measured on an Apple Silicon (arm64) laptop, single process, System.Diagnostics.Stopwatch and GC.GetAllocatedBytesForCurrentThread() only - no BenchmarkDotNet, no statistical rigour beyond reporting every individual run and the median.");
    }

    public static List<QuoteWallItem> ExecuteVariant(string variant, string dbPath) => variant switch
    {
        "EfTracked" => RunEfTracked(dbPath),
        "EfProjection" => RunEfProjection(dbPath),
        "Dapper" => DapperQueries.Run(dbPath, SubmittedSinceUtc),
        _ => throw new ArgumentException($"Unknown variant '{variant}'.")
    };

    private static List<QuoteWallItem> RunEfTracked(string dbPath)
    {
        using var context = new QuotesDbContext(dbPath);
        return EfQueries.RunTracked(context, SubmittedSinceUtc);
    }

    private static List<QuoteWallItem> RunEfProjection(string dbPath)
    {
        using var context = new QuotesDbContext(dbPath);
        return EfQueries.RunProjection(context, SubmittedSinceUtc);
    }

    private static string[] Rotate(string[] source, int offset)
    {
        var rotated = new string[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            rotated[i] = source[(i + offset) % source.Length];
        }
        return rotated;
    }

    private static VariantSummary Summarize(List<IterationRun> runs)
    {
        var elapsedSorted = runs.Select(r => r.ElapsedMs).OrderBy(v => v).ToList();
        var allocatedSorted = runs.Select(r => (double)r.AllocatedBytes).OrderBy(v => v).ToList();
        int rowCount = runs[0].RowCount;

        double medianElapsed = Median(elapsedSorted);
        double medianAllocated = Median(allocatedSorted);

        return new VariantSummary(runs, medianElapsed, medianAllocated, rowCount, medianAllocated / rowCount);
    }

    private static double Median(List<double> sorted)
    {
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    private static string PackageVersion(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            var plusIndex = informational.IndexOf('+');
            return plusIndex >= 0 ? informational[..plusIndex] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}

public sealed record IterationRun(double ElapsedMs, long AllocatedBytes, int RowCount);

public sealed record VariantSummary(
    List<IterationRun> Iterations,
    double MedianElapsedMs,
    double MedianAllocatedBytes,
    int RowCount,
    double AllocatedBytesPerRow);

public sealed record ComparisonResults(
    Dictionary<string, VariantSummary> Variants,
    List<List<string>> IterationOrders,
    int WarmupIterationsPerVariant,
    string DotNetVersion,
    string RuntimeIdentifier,
    string DapperVersion,
    string EfCoreVersion,
    string Notes);
