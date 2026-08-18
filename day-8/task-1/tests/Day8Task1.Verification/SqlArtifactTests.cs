using System.Text.RegularExpressions;
using Xunit;

namespace Day8Task1.Verification;

public class SqlArtifactTests
{
    public static readonly string[] ExpectedSqlFiles =
    [
        "00_create_database.sql",
        "01_schema_heap.sql",
        "02_generate_data.sql",
        "03_queries.sql",
        "10_clustered_index.sql",
        "11_nonclustered_customer.sql",
        "12_nonclustered_covering.sql",
        "20_write_cost_insert.sql",
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
    // explanatory prose (which freely uses words like "primary key" or
    // "INCLUDE" in English) can't be mistaken for actual DDL.
    private static string StripComments(string sql) =>
        string.Join('\n', sql.Replace("\r\n", "\n").Split('\n').Where(l => !l.TrimStart().StartsWith("--")));

    [Fact]
    public void Stage0_heap_schema_creates_no_index_and_no_primary_key()
    {
        var sql = StripComments(File.ReadAllText(Paths.Sql("01_schema_heap.sql")));

        Assert.DoesNotMatch(new Regex(@"PRIMARY\s+KEY", RegexOptions.IgnoreCase), sql);
        Assert.DoesNotMatch(new Regex(@"CREATE\s+(UNIQUE\s+)?(CLUSTERED|NONCLUSTERED)?\s*INDEX", RegexOptions.IgnoreCase), sql);
    }

    [Fact]
    public void Clustered_index_script_creates_exactly_one_clustered_index()
    {
        var sql = StripComments(File.ReadAllText(Paths.Sql("10_clustered_index.sql")));

        var matches = Regex.Matches(sql, @"CREATE\s+CLUSTERED\s+INDEX", RegexOptions.IgnoreCase);
        Assert.Single(matches);
        Assert.DoesNotMatch(new Regex(@"NONCLUSTERED", RegexOptions.IgnoreCase), sql);
    }

    [Fact]
    public void Nonclustered_customer_index_has_no_include_clause()
    {
        var sql = StripComments(File.ReadAllText(Paths.Sql("11_nonclustered_customer.sql")));

        var matches = Regex.Matches(sql, @"CREATE\s+NONCLUSTERED\s+INDEX", RegexOptions.IgnoreCase);
        Assert.Single(matches);
        Assert.DoesNotMatch(new Regex(@"\bINCLUDE\s*\(", RegexOptions.IgnoreCase), sql);
    }

    [Fact]
    public void Nonclustered_covering_index_has_an_include_clause()
    {
        var sql = StripComments(File.ReadAllText(Paths.Sql("12_nonclustered_covering.sql")));

        var matches = Regex.Matches(sql, @"CREATE\s+NONCLUSTERED\s+INDEX", RegexOptions.IgnoreCase);
        Assert.Single(matches);
        Assert.Matches(new Regex(@"\bINCLUDE\s*\(", RegexOptions.IgnoreCase), sql);
    }

    private static List<string> SplitIntoBatches(string sql)
    {
        var lines = sql.Replace("\r\n", "\n").Split('\n');
        var batches = new List<string>();
        var buf = new List<string>();

        foreach (var line in lines)
        {
            if (line.Trim() == "GO")
            {
                var text = string.Join('\n', buf);
                if (!string.IsNullOrWhiteSpace(text))
                    batches.Add(text);
                buf.Clear();
                continue;
            }
            buf.Add(line);
        }
        if (buf.Any(l => !string.IsNullOrWhiteSpace(l)))
            batches.Add(string.Join('\n', buf));

        return batches;
    }

    [Fact]
    public void Queries_file_contains_exactly_three_queries_each_naming_its_target_index()
    {
        var sql = File.ReadAllText(Paths.Sql("03_queries.sql"));
        var batches = SplitIntoBatches(sql)
            .Where(b => !Regex.IsMatch(b.TrimStart(), @"^\s*USE\s", RegexOptions.IgnoreCase))
            .ToList();

        Assert.Equal(3, batches.Count);

        foreach (var batch in batches)
        {
            Assert.Matches(new Regex(@"^\s*--", RegexOptions.Multiline), batch);
            Assert.Matches(new Regex(@"\bSELECT\b", RegexOptions.IgnoreCase), batch);
        }

        Assert.Contains("CIX_Orders_OrderDate", batches[0]);
        Assert.Contains("IX_Orders_CustomerId", batches[1]);
        Assert.DoesNotContain("IX_Orders_CustomerId_Covering", batches[1]);
        Assert.Contains("IX_Orders_CustomerId_Covering", batches[2]);
    }
}
