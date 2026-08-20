using ChangeTrackerDemo;
using Xunit;

namespace ChangeTrackerDemo.Tests;

// Structural verification: reads the actual source of the two query variants and
// proves they differ ONLY by .AsNoTracking() - a silent extra difference (a changed
// Where, OrderBy, or projection) would make this fail.
public class QueryVariantFairnessTests
{
    [Fact]
    public void TrackedAndNoTrackingQueryMethods_DifferOnlyByAsNoTracking()
    {
        string source = File.ReadAllText(TaskPaths.TrackingBenchmarkSourcePath());

        string trackedBody = ExtractMethodBody(source, "ReadAllTracked");
        string noTrackingBody = ExtractMethodBody(source, "ReadAllNoTracking");

        Assert.Contains(".AsNoTracking()", noTrackingBody);
        Assert.DoesNotContain(".AsNoTracking()", trackedBody);

        string normalizedTracked = Normalize(trackedBody);
        string normalizedNoTracking = Normalize(noTrackingBody.Replace(".AsNoTracking()", ""));

        Assert.Equal(normalizedTracked, normalizedNoTracking);
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        int nameIndex = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Could not find method {methodName} in source.");

        int braceOpen = source.IndexOf('{', nameIndex);
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

    private static string Normalize(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));
}
