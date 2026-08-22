using System.Text.Json;

namespace DapperComparison.Tests;

// These parse the REAL files captured by `dotnet run --project DapperComparison -- run-comparison`.
// They will fail (correctly) until that command has been run at least once.
public class ArtefactTests
{
    private static string OutputDir => TaskPaths.OutputDirectory();

    [Fact]
    public void Results_file_has_at_least_five_measured_iterations_and_a_warmup_for_all_three_variants()
    {
        var results = LoadResults();

        Assert.True(results.WarmupIterationsPerVariant >= 1);
        Assert.Equal(3, results.Variants.Count);
        foreach (var (name, summary) in results.Variants)
        {
            Assert.True(summary.Iterations.Count >= 5,
                $"Expected at least 5 measured iterations for {name}, got {summary.Iterations.Count}.");
        }
    }

    [Fact]
    public void All_three_variants_recorded_the_same_row_count()
    {
        var results = LoadResults();

        var rowCounts = results.Variants.Values.Select(v => v.RowCount).Distinct().ToList();
        Assert.Single(rowCounts);
        Assert.Equal(Seeder.QuoteCount, rowCounts[0]);
    }

    [Fact]
    public void Captured_ef_projection_sql_selects_only_the_dto_columns_not_every_quote_column()
    {
        var path = Path.Combine(OutputDir, "ef-projection-sql.log");
        Assert.True(File.Exists(path), $"Expected {path}. Run the comparison first.");

        var text = File.ReadAllText(path);
        var selectLine = text.Split('\n').Single(l => l.TrimStart().StartsWith("SELECT ", StringComparison.Ordinal));

        Assert.DoesNotContain("\"q\".\"AuthorId\"", selectLine);
        Assert.DoesNotContain("\"a\".\"Id\"", selectLine);
        Assert.Contains("\"q\".\"Text\"", selectLine);
        Assert.Contains("\"a\".\"Name\"", selectLine);
        Assert.Contains("\"a\".\"Country\"", selectLine);
    }

    [Fact]
    public void Captured_ef_tracked_sql_exists_and_differs_from_the_projection_sql()
    {
        var trackedPath = Path.Combine(OutputDir, "ef-tracked-sql.log");
        var projectionPath = Path.Combine(OutputDir, "ef-projection-sql.log");
        Assert.True(File.Exists(trackedPath), $"Expected {trackedPath}. Run the comparison first.");
        Assert.True(File.Exists(projectionPath), $"Expected {projectionPath}. Run the comparison first.");

        var trackedText = File.ReadAllText(trackedPath);
        var projectionText = File.ReadAllText(projectionPath);
        Assert.NotEqual(trackedText, projectionText);

        var trackedSelectLine = trackedText.Split('\n').Single(l => l.TrimStart().StartsWith("SELECT ", StringComparison.Ordinal));
        Assert.Contains("\"q\".\"AuthorId\"", trackedSelectLine);
        Assert.Contains("\"a\".\"Id\"", trackedSelectLine);
    }

    [Fact]
    public void Dapper_sql_was_captured_and_shows_the_parameter_placeholder()
    {
        var path = Path.Combine(OutputDir, "dapper-sql.log");
        Assert.True(File.Exists(path), $"Expected {path}. Run the comparison first.");

        var text = File.ReadAllText(path);
        Assert.Contains("SELECT", text);
        Assert.Contains("@SubmittedSinceUtc", text);
    }

    [Fact]
    public void Recorded_dapper_and_ef_core_versions_are_present()
    {
        var results = LoadResults();

        Assert.False(string.IsNullOrWhiteSpace(results.DapperVersion));
        Assert.False(string.IsNullOrWhiteSpace(results.EfCoreVersion));
        Assert.NotEqual("unknown", results.DapperVersion);
        Assert.NotEqual("unknown", results.EfCoreVersion);
    }

    [Fact]
    public void Submission_file_has_all_required_headings_and_a_rule_paragraph()
    {
        var path = TaskPaths.SubmissionFilePath();
        Assert.True(File.Exists(path), $"Expected submission.md at {path}.");

        var text = File.ReadAllText(path);
        Assert.Contains("## GitHub link", text);
        Assert.Contains("## Notes for mentor", text);
        Assert.Contains("## What did you learn this session?", text);
        Assert.Contains("## What would break this?", text);
        Assert.Contains("### The rule", text);
    }

    private static ComparisonResults LoadResults()
    {
        var path = Path.Combine(OutputDir, "results.json");
        Assert.True(File.Exists(path), $"Expected {path}. Run `dotnet run --project DapperComparison -- run-comparison` first.");

        var results = JsonSerializer.Deserialize<ComparisonResults>(File.ReadAllText(path));
        Assert.NotNull(results);
        return results!;
    }
}
