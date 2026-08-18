using System.Text.RegularExpressions;
using Xunit;

namespace Day8Task2.Verification;

public class SqlArtifactTests
{
    public static readonly string[] ExpectedSqlFiles =
    [
        "00_create_database.sql",
        "01_schema.sql",
        "02_generate_data.sql",
        "03_query.sql",
        "10_noncovering_index.sql",
        "11_covering_index.sql",
        "90_teardown.sql",
    ];

    public static IEnumerable<object[]> ExpectedSqlFileNames() =>
        ExpectedSqlFiles.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(ExpectedSqlFileNames))]
    public void Expected_sql_file_exists_and_is_non_empty(string fileName)
    {
        var path = Paths.Sql(fileName);

        Assert.True(File.Exists(path), $"Expected SQL artefact missing: {fileName}");
        var content = File.ReadAllText(path);
        Assert.False(string.IsNullOrWhiteSpace(content), $"{fileName} exists but is empty");
    }

    // Strips full-line `-- comment` lines before running structural checks, so
    // explanatory prose (which freely uses words like "INCLUDE" or index
    // names) can't be mistaken for actual DDL.
    private static string StripComments(string sql) =>
        string.Join('\n', sql.Replace("\r\n", "\n").Split('\n').Where(l => !l.TrimStart().StartsWith("--")));

    private static readonly Regex IndexNameRegex = new(@"CREATE\s+NONCLUSTERED\s+INDEX\s+(\w+)", RegexOptions.IgnoreCase);

    [Fact]
    public void Noncovering_index_is_nonclustered_with_no_include_clause()
    {
        var sql = StripComments(File.ReadAllText(Paths.Sql("10_noncovering_index.sql")));

        var match = IndexNameRegex.Match(sql);
        Assert.True(match.Success, "10_noncovering_index.sql does not contain a CREATE NONCLUSTERED INDEX statement");
        Assert.DoesNotMatch(new Regex(@"\bINCLUDE\s*\(", RegexOptions.IgnoreCase), sql);
    }

    [Fact]
    public void Covering_index_rebuilds_the_same_name_with_drop_existing_and_include()
    {
        var beforeSql = StripComments(File.ReadAllText(Paths.Sql("10_noncovering_index.sql")));
        var afterSql = StripComments(File.ReadAllText(Paths.Sql("11_covering_index.sql")));

        var beforeName = IndexNameRegex.Match(beforeSql);
        var afterName = IndexNameRegex.Match(afterSql);

        Assert.True(afterName.Success, "11_covering_index.sql does not contain a CREATE NONCLUSTERED INDEX statement");
        Assert.Equal(beforeName.Groups[1].Value, afterName.Groups[1].Value);

        Assert.Matches(new Regex(@"DROP_EXISTING\s*=\s*ON", RegexOptions.IgnoreCase), afterSql);
        Assert.Matches(new Regex(@"\bINCLUDE\s*\(([^)]+)\)", RegexOptions.IgnoreCase), afterSql);
    }

    [Fact]
    public void Every_included_column_is_actually_used_by_the_query_under_test()
    {
        var afterSql = StripComments(File.ReadAllText(Paths.Sql("11_covering_index.sql")));
        var querySql = StripComments(File.ReadAllText(Paths.Sql("03_query.sql")));

        var includeMatch = Regex.Match(afterSql, @"\bINCLUDE\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
        Assert.True(includeMatch.Success, "No INCLUDE clause found in 11_covering_index.sql");

        var includedColumns = includeMatch.Groups[1].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.NotEmpty(includedColumns);

        foreach (var column in includedColumns)
        {
            Assert.Matches(new Regex($@"\b{Regex.Escape(column)}\b", RegexOptions.IgnoreCase), querySql);
        }
    }
}
