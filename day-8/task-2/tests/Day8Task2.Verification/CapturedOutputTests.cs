using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Day8Task2.Verification;

public class CapturedOutputTests
{
    public static readonly string[] Stages = ["stage1-before", "stage2-after"];

    public static IEnumerable<object[]> StageArgs() => Stages.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(StageArgs))]
    public void Stats_profile_capture_exists_and_is_non_empty(string stage)
    {
        var path = Paths.StageFile(stage, "query_stats_profile.txt");
        Assert.True(File.Exists(path), $"Missing capture: {stage}/query_stats_profile.txt");
        Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(path)), $"Empty capture: {stage}/query_stats_profile.txt");
    }

    [Theory]
    [MemberData(nameof(StageArgs))]
    public void Stats_profile_capture_contains_a_logical_reads_line(string stage)
    {
        var text = File.ReadAllText(Paths.StageFile(stage, "query_stats_profile.txt"));
        Assert.Matches(new Regex(@"logical reads\s+\d+", RegexOptions.IgnoreCase), text);
    }

    [Theory]
    [MemberData(nameof(StageArgs))]
    public void Plan_capture_exists_and_is_well_formed_xml_with_runtime_actuals(string stage)
    {
        var path = Paths.StageFile(stage, "query_plan.sqlplan");
        Assert.True(File.Exists(path), $"Missing plan capture: {stage}/query_plan.sqlplan");

        var text = File.ReadAllText(path);
        Assert.False(string.IsNullOrWhiteSpace(text), $"Empty plan capture: {stage}/query_plan.sqlplan");

        XDocument doc;
        try
        {
            doc = XDocument.Parse(text);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new Xunit.Sdk.XunitException($"{stage}/query_plan.sqlplan is not well-formed XML: {ex.Message}");
        }

        // An actual (not estimated-only) plan carries per-operator runtime
        // counters. Estimated-only plans (SHOWPLAN_XML) never contain this.
        Assert.Contains("RunTimeCountersPerThread", text);
        Assert.Contains("ActualRows", text);
    }
}
