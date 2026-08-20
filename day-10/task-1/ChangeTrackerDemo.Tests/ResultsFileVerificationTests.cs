using System.Text.Json;
using ChangeTrackerDemo;
using Xunit;

namespace ChangeTrackerDemo.Tests;

// Verifies the machine-readable evidence file produced by `dotnet run --project
// ChangeTrackerDemo`: it must actually exist (these tests do not generate it - they
// only read whatever the real run produced) and satisfy the measurement-hygiene rules.
public class ResultsFileVerificationTests
{
    [Fact]
    public void ResultsFile_Exists()
    {
        string path = TaskPaths.ResultsFilePath();
        Assert.True(File.Exists(path),
            $"Expected results file at {path}. Run `dotnet run --project ChangeTrackerDemo` first.");
    }

    [Fact]
    public void ResultsFile_HasAtLeastFiveMeasuredIterations_PlusDiscardedWarmup_ForBothVariants()
    {
        var report = LoadReport();

        Assert.True(report.Tracked.MeasuredIterations.Count >= 5);
        Assert.True(report.NoTracking.MeasuredIterations.Count >= 5);
        Assert.True(report.Tracked.WarmupIterations.Count >= 1);
        Assert.True(report.NoTracking.WarmupIterations.Count >= 1);
    }

    [Fact]
    public void ResultsFile_BothVariants_ReportSameRowCount_TenThousand()
    {
        var report = LoadReport();

        int trackedRowCount = report.Tracked.MeasuredIterations[0].RowCount;
        int noTrackingRowCount = report.NoTracking.MeasuredIterations[0].RowCount;

        Assert.Equal(10_000, trackedRowCount);
        Assert.Equal(10_000, noTrackingRowCount);
        Assert.Equal(trackedRowCount, noTrackingRowCount);

        Assert.All(report.Tracked.MeasuredIterations, m => Assert.Equal(10_000, m.RowCount));
        Assert.All(report.NoTracking.MeasuredIterations, m => Assert.Equal(10_000, m.RowCount));
    }

    private static BenchmarkReport LoadReport()
    {
        string json = File.ReadAllText(TaskPaths.ResultsFilePath());
        return JsonSerializer.Deserialize<BenchmarkReport>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidOperationException("results.json deserialised to null.");
    }
}
