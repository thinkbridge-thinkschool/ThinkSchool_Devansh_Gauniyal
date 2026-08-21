using System.Text.RegularExpressions;

namespace FastApi.Tests;

// These tests parse the REAL files captured by scripts/run-profile.sh, plus task-1's
// committed "before" files (read-only - never written to). They will fail (correctly)
// until run-profile.sh has been run at least once.
public class ArtefactTests
{
    private static string OutputDir => TaskPaths.OutputDirectory();
    private static string Task1OutputDir => TaskPaths.Task1OutputDirectory();

    [Fact]
    public void After_load_test_output_contains_parseable_p50_and_p99()
    {
        var path = Path.Combine(OutputDir, "load-test-projection.txt");
        Assert.True(File.Exists(path), $"Expected after load test output at {path}. Run scripts/run-profile.sh first.");

        var text = File.ReadAllText(path);
        Assert.NotNull(LatencyPercentileParser.ParsePercentileMilliseconds(text, "50%"));
        Assert.NotNull(LatencyPercentileParser.ParsePercentileMilliseconds(text, "99%"));
    }

    [Fact]
    public void Task1_committed_baseline_output_is_readable_and_its_p99_is_parseable()
    {
        var path = Path.Combine(Task1OutputDir, "load-test.txt");
        Assert.True(File.Exists(path), $"Expected task-1's committed baseline at {path}.");

        var text = File.ReadAllText(path);
        var beforeP99 = LatencyPercentileParser.ParsePercentileMilliseconds(text, "99%");
        Assert.NotNull(beforeP99);
    }

    [Fact]
    public void Load_parameters_match_task1_field_by_field()
    {
        var task1LoadTestPath = Path.Combine(Task1OutputDir, "load-test.txt");
        var task2LoadTestPath = Path.Combine(OutputDir, "load-test-projection.txt");
        Assert.True(File.Exists(task1LoadTestPath), $"Expected task-1's committed baseline at {task1LoadTestPath}.");
        Assert.True(File.Exists(task2LoadTestPath), $"Expected task-2's after output at {task2LoadTestPath}. Run scripts/run-profile.sh first.");

        var task1Text = File.ReadAllText(task1LoadTestPath);
        var task2Text = File.ReadAllText(task2LoadTestPath);

        var task1Concurrency = LatencyPercentileParser.ExtractFlag(task1Text, "-c");
        var task2Concurrency = LatencyPercentileParser.ExtractFlag(task2Text, "-c");
        Assert.NotNull(task1Concurrency);
        Assert.Equal(task1Concurrency, task2Concurrency);

        var task1Duration = LatencyPercentileParser.ExtractFlag(task1Text, "-d");
        var task2Duration = LatencyPercentileParser.ExtractFlag(task2Text, "-d");
        Assert.NotNull(task1Duration);
        Assert.Equal(task1Duration, task2Duration);

        var task1EnvPath = Path.Combine(Task1OutputDir, "environment.txt");
        var task2EnvPath = Path.Combine(OutputDir, "environment.txt");
        Assert.True(File.Exists(task1EnvPath));
        Assert.True(File.Exists(task2EnvPath), "Expected task-2's environment.txt. Run scripts/run-profile.sh first.");

        var task1ToolVersion = ExtractToolVersionLine(File.ReadAllText(task1EnvPath));
        var task2ToolVersion = ExtractToolVersionLine(File.ReadAllText(task2EnvPath));
        Assert.NotNull(task1ToolVersion);
        Assert.Equal(task1ToolVersion, task2ToolVersion);

        var task1RowCount = ExtractAuthorsReturned(File.ReadAllText(Path.Combine(Task1OutputDir, "sql-sample.log")));
        var task2RowCount = ExtractAuthorsReturned(File.ReadAllText(Path.Combine(OutputDir, "sql-sample-projection.log")));
        Assert.NotNull(task1RowCount);
        Assert.Equal(task1RowCount, task2RowCount);
        Assert.Equal(Seeder.AuthorCount, task2RowCount);
    }

    [Fact]
    public void After_query_plan_for_projection_shows_search_with_index_and_no_scan_of_quotes()
    {
        var path = Path.Combine(OutputDir, "query-plan-projection.txt");
        Assert.True(File.Exists(path), $"Expected after query plan at {path}. Run scripts/run-profile.sh first.");

        var text = File.ReadAllText(path);
        Assert.Contains("SEARCH q USING", text);
        Assert.Contains("INDEX", text);
        Assert.DoesNotContain("SCAN q", text);
    }

    [Fact]
    public void After_query_plan_for_split_query_shows_search_with_index_and_no_scan_of_quotes()
    {
        var path = Path.Combine(OutputDir, "query-plan-split.txt");
        Assert.True(File.Exists(path), $"Expected after query plan at {path}. Run scripts/run-profile.sh first.");

        var text = File.ReadAllText(path);
        Assert.Contains("SEARCH q USING", text);
        Assert.Contains("INDEX", text);
        Assert.DoesNotContain("SCAN q", text);
    }

    [Fact]
    public void Captured_schema_dump_lists_the_index_on_author_id()
    {
        var path = Path.Combine(OutputDir, "schema-dump.txt");
        Assert.True(File.Exists(path), $"Expected schema dump at {path}. Run scripts/run-profile.sh first.");

        var text = File.ReadAllText(path);
        var indexSectionStart = text.IndexOf("Indexes that exist on the Quotes table", StringComparison.Ordinal);
        Assert.True(indexSectionStart >= 0);

        var indexSection = text[indexSectionStart..];
        Assert.Contains("AuthorId", indexSection);
        Assert.DoesNotContain("no indexes found", indexSection);
    }

    [Fact]
    public void Submission_file_has_all_required_headings_and_both_before_and_after_p99_figures()
    {
        var path = TaskPaths.SubmissionFilePath();
        Assert.True(File.Exists(path), $"Expected submission.md at {path}.");

        var text = File.ReadAllText(path);

        Assert.Contains("## GitHub link", text);
        Assert.Contains("## Notes for mentor", text);
        Assert.Contains("## What did you learn this session?", text);
        Assert.Contains("## What would break this?", text);

        var beforeText = File.ReadAllText(Path.Combine(Task1OutputDir, "load-test.txt"));
        var afterPath = Path.Combine(OutputDir, "load-test-projection.txt");
        Assert.True(File.Exists(afterPath), "Run scripts/run-profile.sh first.");
        var afterText = File.ReadAllText(afterPath);

        var beforeP99 = LatencyPercentileParser.ParsePercentileMilliseconds(beforeText, "99%");
        var afterP99 = LatencyPercentileParser.ParsePercentileMilliseconds(afterText, "99%");
        Assert.NotNull(beforeP99);
        Assert.NotNull(afterP99);

        Assert.Contains(beforeP99.Value.ToString("0.00"), text);
        Assert.Contains(afterP99.Value.ToString("0.00"), text);
    }

    private static string? ExtractToolVersionLine(string environmentText)
    {
        var match = Regex.Match(environmentText, @"bombardier version [^\r\n]+");
        return match.Success ? match.Value : null;
    }

    private static int? ExtractAuthorsReturned(string sqlSampleText)
    {
        var match = Regex.Match(sqlSampleText, @"Authors returned:\s*(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }
}
