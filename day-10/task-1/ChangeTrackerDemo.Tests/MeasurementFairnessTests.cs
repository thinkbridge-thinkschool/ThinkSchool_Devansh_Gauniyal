using ChangeTrackerDemo;
using Xunit;

namespace ChangeTrackerDemo.Tests;

// Structural verification of the single most important fairness property: the
// measurement harness must construct a NEW DbContext inside the per-iteration method,
// not once outside the loop - otherwise the tracked variant's second read would get
// identity resolution for free and the comparison would be meaningless.
public class MeasurementFairnessTests
{
    [Fact]
    public void MeasureOnce_ConstructsNewDbContextInsideItself()
    {
        string source = File.ReadAllText(TaskPaths.TrackingBenchmarkSourcePath());
        string measureOnceBody = ExtractMethodBody(source, "private static IterationMeasurement MeasureOnce");

        Assert.Contains("new CatalogContext(", measureOnceBody);
    }

    [Fact]
    public void Run_DoesNotConstructDbContextItself_OnlyMeasureOnceDoes()
    {
        string source = File.ReadAllText(TaskPaths.TrackingBenchmarkSourcePath());
        string runBody = ExtractMethodBody(source, "public static BenchmarkReport Run");

        Assert.DoesNotContain("new CatalogContext(", runBody);
    }

    private static string ExtractMethodBody(string source, string methodSignaturePrefix)
    {
        int start = source.IndexOf(methodSignaturePrefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method starting with '{methodSignaturePrefix}'.");

        int braceOpen = source.IndexOf('{', start);
        int depth = 0;
        int i = braceOpen;
        for (; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) break;
            }
        }

        return source[braceOpen..(i + 1)];
    }
}
