namespace CqrsLite.Tests;

// These parse the REAL files captured by `dotnet run -- capture-sql` (see CqrsLite/README.md).
// They will fail (correctly) until that command has been run at least once.
public class ArtefactTests
{
    private static string OutputDir => TaskPaths.OutputDirectory();

    [Fact]
    public void Command_sql_log_exists_and_contains_an_insert()
    {
        var path = Path.Combine(OutputDir, "command-sql.log");
        Assert.True(File.Exists(path), $"Expected {path}. Run `dotnet run -- capture-sql` first.");

        var text = File.ReadAllText(path);
        Assert.Contains("INSERT INTO \"Quotes\"", text);
    }

    [Fact]
    public void Query_sql_log_exists_and_its_select_list_excludes_columns_the_read_model_does_not_need()
    {
        var path = Path.Combine(OutputDir, "query-sql.log");
        Assert.True(File.Exists(path), $"Expected {path}. Run `dotnet run -- capture-sql` first.");

        var text = File.ReadAllText(path);
        var selectLine = text.Split('\n').Single(line => line.TrimStart().StartsWith("SELECT ", StringComparison.Ordinal));

        Assert.DoesNotContain("*", selectLine);
        // Quote.AuthorId is a real Quote column the read model has no field for - the join
        // key is used to join, but never surfaces in the select list.
        Assert.DoesNotContain("\"q\".\"AuthorId\"", selectLine);
        // Author.Id is a real Author column the read model has no field for either.
        Assert.DoesNotContain("\"a\".\"Id\"", selectLine);

        Assert.Contains("\"q\".\"Text\"", selectLine);
        Assert.Contains("\"a\".\"Name\"", selectLine);
        Assert.Contains("\"a\".\"Country\"", selectLine);
    }

    [Fact]
    public void Command_and_query_sql_logs_genuinely_differ()
    {
        var commandPath = Path.Combine(OutputDir, "command-sql.log");
        var queryPath = Path.Combine(OutputDir, "query-sql.log");
        Assert.True(File.Exists(commandPath), $"Expected {commandPath}. Run `dotnet run -- capture-sql` first.");
        Assert.True(File.Exists(queryPath), $"Expected {queryPath}. Run `dotnet run -- capture-sql` first.");

        var commandText = File.ReadAllText(commandPath);
        var queryText = File.ReadAllText(queryPath);

        Assert.NotEqual(commandText, queryText);
        Assert.Contains("INSERT INTO", commandText);
        Assert.DoesNotContain("INSERT INTO", queryText);
    }

    [Fact]
    public void Validation_outcomes_file_documents_the_successful_case_and_every_rejected_case()
    {
        var path = Path.Combine(OutputDir, "validation-outcomes.txt");
        Assert.True(File.Exists(path), $"Expected {path}. Run `dotnet run -- capture-sql` first.");

        var text = File.ReadAllText(path);
        Assert.Contains("FailureReason=None", text);
        Assert.Contains("FailureReason=TextEmpty", text);
        Assert.Contains("FailureReason=TextTooLong", text);
        Assert.Contains("FailureReason=AuthorNotFound", text);
        Assert.Contains("FailureReason=DuplicateQuote", text);
    }

    [Fact]
    public void Submission_file_has_all_four_required_headings()
    {
        var path = TaskPaths.SubmissionFilePath();
        Assert.True(File.Exists(path), $"Expected submission.md at {path}.");

        var text = File.ReadAllText(path);
        Assert.Contains("## GitHub link", text);
        Assert.Contains("## Notes for mentor", text);
        Assert.Contains("## What did you learn this session?", text);
        Assert.Contains("## What would break this?", text);
    }
}
