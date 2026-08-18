using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Day8Task1.Verification;

public class CapturedOutputTests
{
    public static readonly string[] QueryStages =
    [
        "stage0-heap",
        "stage1-clustered",
        "stage2-nc-customer",
        "stage3-nc-covering",
    ];

    public static readonly string[] WriteCostStages =
    [
        "writecost-clustered-only",
        "writecost-all-indexes",
    ];

    public static IEnumerable<object[]> StageAndQuery()
    {
        foreach (var stage in QueryStages)
            foreach (var q in new[] { 1, 2, 3 })
                yield return new object[] { stage, q };
    }

    [Theory]
    [MemberData(nameof(StageAndQuery))]
    public void Stats_profile_capture_exists_for_every_stage_and_query(string stage, int q)
    {
        var path = Paths.StageFile(stage, $"q{q}_stats_profile.txt");
        Assert.True(File.Exists(path), $"Missing capture: {stage}/q{q}_stats_profile.txt");
        Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(path)), $"Empty capture: {stage}/q{q}_stats_profile.txt");
    }

    [Theory]
    [MemberData(nameof(StageAndQuery))]
    public void Stats_profile_capture_contains_a_logical_reads_line(string stage, int q)
    {
        var path = Paths.StageFile(stage, $"q{q}_stats_profile.txt");
        var text = File.ReadAllText(path);

        Assert.Matches(new Regex(@"logical reads\s+\d+", RegexOptions.IgnoreCase), text);
    }

    [Theory]
    [MemberData(nameof(StageAndQuery))]
    public void Plan_capture_exists_and_is_well_formed_xml_with_runtime_actuals(string stage, int q)
    {
        var path = Paths.StageFile(stage, $"q{q}_plan.sqlplan");
        Assert.True(File.Exists(path), $"Missing plan capture: {stage}/q{q}_plan.sqlplan");

        var text = File.ReadAllText(path);
        Assert.False(string.IsNullOrWhiteSpace(text), $"Empty plan capture: {stage}/q{q}_plan.sqlplan");

        XDocument doc;
        try
        {
            doc = XDocument.Parse(text);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new Xunit.Sdk.XunitException($"{stage}/q{q}_plan.sqlplan is not well-formed XML: {ex.Message}");
        }

        // An actual (not estimated-only) plan carries per-operator runtime
        // counters. Estimated-only plans (SHOWPLAN_XML) never contain this.
        Assert.Contains("RunTimeCountersPerThread", text);
        Assert.Contains("ActualRows", text);
    }

    [Theory]
    [MemberData(nameof(WriteCostStageArgs))]
    public void Write_cost_capture_exists_with_io_and_time_stats(string stage)
    {
        var path = Paths.StageFile(stage, "insert_stats.txt");
        Assert.True(File.Exists(path), $"Missing write-cost capture: {stage}/insert_stats.txt");

        var text = File.ReadAllText(path);
        Assert.Matches(new Regex(@"logical reads\s+\d+", RegexOptions.IgnoreCase), text);
        Assert.Matches(new Regex(@"CPU time\s*=\s*\d+", RegexOptions.IgnoreCase), text);
    }

    public static IEnumerable<object[]> WriteCostStageArgs() => WriteCostStages.Select(s => new object[] { s });
}
