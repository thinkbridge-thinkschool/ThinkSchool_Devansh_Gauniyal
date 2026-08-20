using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;

namespace ChangeTrackerDemo;

// The two query variants the Academy exercise asks to be pasted. They are adjacent,
// identically shaped, and differ ONLY by the .AsNoTracking() call - same filter (none),
// same ordering, same projection (whole-entity materialisation), same row count.
public static class QueryVariants
{
    public static List<Product> ReadAllTracked(CatalogContext context)
    {
        return context.Products
            .OrderBy(p => p.Id)
            .ToList();
    }

    public static List<Product> ReadAllNoTracking(CatalogContext context)
    {
        return context.Products
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToList();
    }
}

public record IterationMeasurement(int Iteration, double ElapsedMilliseconds, long AllocatedBytes, int RowCount);

public record VariantReport(
    string Variant,
    List<IterationMeasurement> WarmupIterations,
    List<IterationMeasurement> MeasuredIterations,
    double MedianElapsedMilliseconds,
    long MedianAllocatedBytes,
    double MedianAllocatedBytesPerEntity);

public record BenchmarkReport(
    string GeneratedAtUtc,
    string DotNetVersion,
    string RuntimeIdentifier,
    string Architecture,
    int WarmupIterationCount,
    int MeasuredIterationCount,
    VariantReport Tracked,
    VariantReport NoTracking);

public static class TrackingBenchmark
{
    private const int WarmupIterationCount = 1;
    private const int MeasuredIterationCount = 5;

    public static BenchmarkReport Run(string dbPath)
    {
        var trackedWarmup = new List<IterationMeasurement>();
        var noTrackingWarmup = new List<IterationMeasurement>();
        for (int i = 0; i < WarmupIterationCount; i++)
        {
            trackedWarmup.Add(MeasureOnce(dbPath, i, tracked: true));
            noTrackingWarmup.Add(MeasureOnce(dbPath, i, tracked: false));
        }

        var trackedRuns = new List<IterationMeasurement>();
        var noTrackingRuns = new List<IterationMeasurement>();
        for (int i = 0; i < MeasuredIterationCount; i++)
        {
            trackedRuns.Add(MeasureOnce(dbPath, i, tracked: true));
            noTrackingRuns.Add(MeasureOnce(dbPath, i, tracked: false));
        }

        return new BenchmarkReport(
            GeneratedAtUtc: DateTime.UtcNow.ToString("O"),
            DotNetVersion: Environment.Version.ToString(),
            RuntimeIdentifier: RuntimeInformation.RuntimeIdentifier,
            Architecture: RuntimeInformation.ProcessArchitecture.ToString(),
            WarmupIterationCount: WarmupIterationCount,
            MeasuredIterationCount: MeasuredIterationCount,
            Tracked: BuildVariantReport("Tracked", trackedWarmup, trackedRuns),
            NoTracking: BuildVariantReport("AsNoTracking", noTrackingWarmup, noTrackingRuns));
    }

    private static IterationMeasurement MeasureOnce(string dbPath, int iteration, bool tracked)
    {
        // Collect before every measured iteration so the previous iteration's garbage
        // (e.g. the tracked variant's discarded snapshots) is never charged to this
        // iteration's allocation count.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // A brand-new DbContext per iteration is the single most important fairness
        // property here: reusing one context would let the second tracked read resolve
        // identities for free from the first read, which would make the tracked variant
        // look artificially cheap and invalidate the whole comparison.
        using var context = new CatalogContext(dbPath);
        var rows = tracked
            ? QueryVariants.ReadAllTracked(context)
            : QueryVariants.ReadAllNoTracking(context);

        stopwatch.Stop();
        long after = GC.GetAllocatedBytesForCurrentThread();

        return new IterationMeasurement(iteration, stopwatch.Elapsed.TotalMilliseconds, after - before, rows.Count);
    }

    private static VariantReport BuildVariantReport(
        string name, List<IterationMeasurement> warmup, List<IterationMeasurement> measured)
    {
        double medianElapsed = Median(measured.Select(m => m.ElapsedMilliseconds));
        long medianAllocated = (long)Median(measured.Select(m => (double)m.AllocatedBytes));
        int rowCount = measured[0].RowCount;
        double perEntity = rowCount > 0 ? medianAllocated / (double)rowCount : 0;

        return new VariantReport(name, warmup, measured, medianElapsed, medianAllocated, perEntity);
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }
}
