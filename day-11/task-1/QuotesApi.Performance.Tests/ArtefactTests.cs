using System.Text.RegularExpressions;

namespace QuotesApi.Performance.Tests;

// These tests parse the REAL files captured by scripts/run-profile.sh. They will fail
// (correctly) until that script has been run at least once, since there is nothing to
// verify without a real load test having actually happened.
public class ArtefactTests
{
    private static string OutputDir => TaskPaths.OutputDirectory();

    [Fact]
    public void Load_test_output_contains_parseable_p50_and_p99()
    {
        var path = Path.Combine(OutputDir, "load-test.txt");
        Assert.True(File.Exists(path), $"Expected load test output at {path}. Run scripts/run-profile.sh first.");

        var text = File.ReadAllText(path);
        var p50 = LatencyPercentileParser.ParsePercentileMilliseconds(text, "50%");
        var p99 = LatencyPercentileParser.ParsePercentileMilliseconds(text, "99%");

        Assert.NotNull(p50);
        Assert.NotNull(p99);
    }

    [Fact]
    public void Load_test_p99_exceeds_p50()
    {
        var path = Path.Combine(OutputDir, "load-test.txt");
        Assert.True(File.Exists(path), $"Expected load test output at {path}. Run scripts/run-profile.sh first.");

        var text = File.ReadAllText(path);
        var p50 = LatencyPercentileParser.ParsePercentileMilliseconds(text, "50%");
        var p99 = LatencyPercentileParser.ParsePercentileMilliseconds(text, "99%");

        Assert.NotNull(p50);
        Assert.NotNull(p99);
        Assert.True(p99!.Value > p50!.Value, $"Expected p99 ({p99}ms) to exceed p50 ({p50}ms).");
    }

    [Fact]
    public void Sql_sample_log_contains_more_than_author_count_statements()
    {
        var path = Path.Combine(OutputDir, "sql-sample.log");
        Assert.True(File.Exists(path), $"Expected SQL sample log at {path}. Run scripts/run-profile.sh first.");

        var text = File.ReadAllText(path);
        var statementCount = Regex.Matches(text, "Executed DbCommand").Count;

        Assert.True(statementCount > PerformanceSeeder.AuthorCount,
            $"Expected more than {PerformanceSeeder.AuthorCount} executed statements (1 + N), found {statementCount}.");
    }

    [Fact]
    public void Query_plan_shows_a_scan_not_a_search()
    {
        var path = Path.Combine(OutputDir, "query-plan.txt");
        Assert.True(File.Exists(path), $"Expected query plan output at {path}. Run scripts/run-profile.sh first.");

        var text = File.ReadAllText(path);

        Assert.Contains("SCAN", text);
        Assert.DoesNotContain("SEARCH", text);
    }

    [Fact]
    public void Schema_dump_lists_no_index_on_author_id()
    {
        var path = Path.Combine(OutputDir, "schema-dump.txt");
        Assert.True(File.Exists(path), $"Expected schema dump at {path}. Run scripts/run-profile.sh first.");

        var text = File.ReadAllText(path);
        var indexSectionStart = text.IndexOf("Indexes that exist on the Quotes table", StringComparison.Ordinal);
        Assert.True(indexSectionStart >= 0, "Expected an index-listing section in the schema dump.");

        var indexSection = text[indexSectionStart..];
        Assert.DoesNotContain("AuthorId", indexSection);
    }

    [Fact]
    public void Environment_file_records_tool_name_and_version()
    {
        var path = Path.Combine(OutputDir, "environment.txt");
        Assert.True(File.Exists(path), $"Expected environment info at {path}. Run scripts/run-profile.sh first.");

        var text = File.ReadAllText(path);
        Assert.True(
            text.Contains("bombardier", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("k6", StringComparison.OrdinalIgnoreCase),
            "Expected the load-test tool name to be recorded.");
    }

    [Fact]
    public void Submission_file_has_all_required_headings_and_states_two_problems()
    {
        var path = TaskPaths.SubmissionFilePath();
        Assert.True(File.Exists(path), $"Expected submission.md at {path}.");

        var text = File.ReadAllText(path);

        Assert.Contains("## GitHub link", text);
        Assert.Contains("## Notes for mentor", text);
        Assert.Contains("## What did you learn this session?", text);
        Assert.Contains("## What would break this?", text);

        Assert.Contains("N+1", text);
        Assert.Contains("index", text, StringComparison.OrdinalIgnoreCase);
    }
}
