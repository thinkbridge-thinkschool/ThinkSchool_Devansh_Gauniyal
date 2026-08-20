using System.Text.Json;
using QueryTranslationDemo;
using Xunit;

namespace QueryTranslationDemo.Tests;

// These deliberately re-read output/evidence.json and the Queries.cs source from disk
// rather than trusting QueryEvidenceFixture's in-memory object - the file on disk is the
// real evidence a mentor can inspect independently of this test run.
[Collection("QueryEvidence")]
public class VerificationTests
{
    public VerificationTests(QueryEvidenceFixture fixture)
    {
        // constructor parameter only orders this class after the fixture within the
        // shared collection; the fixture's file-writing side effect is what matters.
        _ = fixture;
    }

    [Fact]
    public void EvidenceFile_Exists_AndIsWellFormed()
    {
        Assert.True(File.Exists(TaskPaths.EvidenceFilePath()));
        var report = LoadReport();
        Assert.False(string.IsNullOrWhiteSpace(report.EfCoreVersion));
    }

    [Fact]
    public void AfterSql_NamesFewerColumns_ThanBeforeSql()
    {
        var report = LoadReport();
        int beforeColumns = CountSelectColumns(report.Before.RawSql);
        int afterColumns = CountSelectColumns(report.After.RawSql);

        Assert.True(
            afterColumns < beforeColumns,
            $"Expected AFTER ({afterColumns} columns) to select fewer columns than BEFORE ({beforeColumns} columns).");
    }

    [Fact]
    public void AfterSql_DoesNotContainDescriptionColumn_ButBeforeSqlDoes()
    {
        var report = LoadReport();
        Assert.Contains("\"Description\"", report.Before.RawSql);
        Assert.DoesNotContain("\"Description\"", report.After.RawSql);
    }

    [Fact]
    public void FixedSql_ContainsWhereClause()
    {
        var report = LoadReport();
        Assert.Contains("WHERE", report.FixedQuery.RawSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BeforeAndAfterQueryMethods_DifferOnlyInProjection()
    {
        string source = File.ReadAllText(TaskPaths.QueriesSourcePath());
        string beforeBody = ExtractMethodBody(source, "ReadProductsAboveMinPrice_WholeEntities");
        string afterBody = ExtractMethodBody(source, "ReadProductsAboveMinPrice_Projected");

        Assert.Contains(".Select(", afterBody);
        Assert.DoesNotContain(".Select(", beforeBody);

        string afterWithoutSelect = RemoveSelectCall(afterBody);
        Assert.Equal(Normalize(beforeBody), Normalize(afterWithoutSelect));
    }

    [Fact]
    public void CapturedException_IsInvalidOperationException()
    {
        var report = LoadReport();
        Assert.Equal(typeof(InvalidOperationException).FullName, report.Broken.ExceptionType);
    }

    [Fact]
    public void CapturedOutput_RecordsEfCoreVersion()
    {
        var report = LoadReport();
        Assert.False(string.IsNullOrWhiteSpace(report.EfCoreVersion));
    }

    [Fact]
    public void SubmissionMarkdown_Exists_AndHasAllRequiredHeadings()
    {
        string path = TaskPaths.SubmissionFilePath();
        Assert.True(File.Exists(path), $"submission.md not found at {path}");

        string content = File.ReadAllText(path);
        Assert.Contains("## GitHub link", content);
        Assert.Contains("## Notes for mentor", content);
        Assert.Contains("## What did you learn this session?", content);
        Assert.Contains("## What would break this?", content);
    }

    [Fact]
    public void NoFileUnderTaskRoot_ContainsACredentialLookingPattern()
    {
        string[] forbiddenPatterns = { "password=", "pwd=", "Server=tcp:", "AccountKey=", "-----BEGIN" };
        string root = TaskPaths.FindTaskRoot();

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && Path.GetFileName(f) != "VerificationTests.cs");

        foreach (var file in files)
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var pattern in forbiddenPatterns)
            {
                Assert.False(
                    text.Contains(pattern, StringComparison.OrdinalIgnoreCase),
                    $"File {file} appears to contain a credential-looking pattern: {pattern}");
            }
        }
    }

    private static EvidenceReport LoadReport()
    {
        string json = File.ReadAllText(TaskPaths.EvidenceFilePath());
        return JsonSerializer.Deserialize<EvidenceReport>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidOperationException("evidence.json deserialised to null.");
    }

    private static int CountSelectColumns(string sql)
    {
        int selectIndex = sql.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
        int fromIndex = sql.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
        Assert.True(selectIndex >= 0 && fromIndex > selectIndex, "Could not find a SELECT ... FROM segment.");

        string columnList = sql[(selectIndex + "SELECT".Length)..fromIndex];
        return columnList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
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

    private static string RemoveSelectCall(string body)
    {
        int selectIndex = body.IndexOf(".Select(", StringComparison.Ordinal);
        if (selectIndex < 0)
        {
            return body;
        }

        int parenStart = selectIndex + ".Select".Length;
        int depth = 0;
        int i = parenStart;
        for (; i < body.Length; i++)
        {
            if (body[i] == '(') depth++;
            else if (body[i] == ')')
            {
                depth--;
                if (depth == 0) break;
            }
        }

        return body.Remove(selectIndex, (i + 1) - selectIndex);
    }

    private static string Normalize(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));
}
