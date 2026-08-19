using System.Text.RegularExpressions;
using Xunit;

namespace Day9Task2.Verification;

public class SqlArtifactTests
{
    public static readonly string[] ExpectedSqlFiles =
    [
        "00_create_database.sql",
        "01_schema.sql",
        "02_seed.sql",
        "03_enable_traceflag_1222.sql",
        "04_disable_traceflag_1222.sql",
        "10_deadlock_sessionA.sql",
        "11_deadlock_sessionB.sql",
        "20_fixed_sessionA.sql",
        "21_fixed_sessionB.sql",
        "30_capture_deadlock_xevents.sql",
        "31_capture_deadlock_errorlog.sql",
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

    // Strips SQL line comments (-- ...) and blank lines, returning the
    // remaining non-empty statement/keyword lines in order. Used to compare
    // the two session scripts on their actual executable content, ignoring
    // narrative comments that are free to differ between the broken and
    // fixed versions.
    private static List<string> ExecutableLines(string sqlFileName)
    {
        var lines = File.ReadAllLines(Paths.Sql(sqlFileName));
        var result = new List<string>();
        foreach (var raw in lines)
        {
            var line = Regex.Replace(raw, @"--.*$", "").Trim();
            if (line.Length == 0) continue;
            if (string.Equals(line, "GO", StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(line);
        }
        return result;
    }

    private static int FirstTableIndex(List<string> lines, string table)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (Regex.IsMatch(lines[i], $@"\bUPDATE\s+dbo\.{table}\b", RegexOptions.IgnoreCase))
                return i;
        }
        return -1;
    }

    [Fact]
    public void Broken_pair_updates_Accounts_and_Orders_in_opposite_orders()
    {
        var a = ExecutableLines("10_deadlock_sessionA.sql");
        var b = ExecutableLines("11_deadlock_sessionB.sql");

        var aAccounts = FirstTableIndex(a, "Accounts");
        var aOrders = FirstTableIndex(a, "Orders");
        var bAccounts = FirstTableIndex(b, "Accounts");
        var bOrders = FirstTableIndex(b, "Orders");

        Assert.True(aAccounts >= 0 && aOrders >= 0, "Session A (broken) must UPDATE both dbo.Accounts and dbo.Orders");
        Assert.True(bAccounts >= 0 && bOrders >= 0, "Session B (broken) must UPDATE both dbo.Accounts and dbo.Orders");

        Assert.True(aAccounts < aOrders, "Session A (broken) must lock Accounts before Orders");
        Assert.True(bOrders < bAccounts, "Session B (broken) must lock Orders before Accounts - the reverse of Session A - or no circular wait is possible");
    }

    [Fact]
    public void Fixed_pair_updates_Accounts_and_Orders_in_the_same_order()
    {
        var a = ExecutableLines("20_fixed_sessionA.sql");
        var b = ExecutableLines("21_fixed_sessionB.sql");

        var aAccounts = FirstTableIndex(a, "Accounts");
        var aOrders = FirstTableIndex(a, "Orders");
        var bAccounts = FirstTableIndex(b, "Accounts");
        var bOrders = FirstTableIndex(b, "Orders");

        Assert.True(aAccounts >= 0 && aOrders >= 0, "Session A (fixed) must UPDATE both dbo.Accounts and dbo.Orders");
        Assert.True(bAccounts >= 0 && bOrders >= 0, "Session B (fixed) must UPDATE both dbo.Accounts and dbo.Orders");

        Assert.True(aAccounts < aOrders, "Session A (fixed) must lock Accounts before Orders");
        Assert.True(bAccounts < bOrders, "Session B (fixed) must also lock Accounts before Orders - the same order as Session A - or the cycle is not actually eliminated");
    }

    [Fact]
    public void Fixed_session_A_is_identical_to_broken_session_A_in_every_executable_statement()
    {
        // Session A's order was never the problem (see README.md) - the fix
        // is entirely in Session B. This asserts Session A's statements did
        // not change at all between the broken and fixed scripts.
        var broken = ExecutableLines("10_deadlock_sessionA.sql");
        var fixedVersion = ExecutableLines("20_fixed_sessionA.sql");

        Assert.Equal(broken, fixedVersion);
    }

    [Fact]
    public void Fixed_session_B_differs_from_broken_session_B_only_in_statement_order()
    {
        // Interpretation 4 (README.md / submission.md): the ONLY difference
        // between the broken and fixed Session B scripts must be the ORDER
        // of the two UPDATE statements - no added hint, isolation-level
        // change, retry loop, or DEADLOCK_PRIORITY. This is checked two
        // ways: the two statement lists must be equal as sets (same
        // statements, nothing added or removed) but different as sequences
        // (the order must genuinely have changed, not be a no-op edit).
        var broken = ExecutableLines("11_deadlock_sessionB.sql");
        var fixedVersion = ExecutableLines("21_fixed_sessionB.sql");

        var brokenSorted = broken.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var fixedSorted = fixedVersion.OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(brokenSorted, fixedSorted);

        Assert.NotEqual(broken, fixedVersion);

        Assert.Equal(broken.Count, fixedVersion.Count);
        var differingPositions = Enumerable.Range(0, broken.Count)
            .Count(i => broken[i] != fixedVersion[i]);
        Assert.Equal(2, differingPositions); // exactly the two swapped UPDATE lines

        var brokenAccounts = FirstTableIndex(broken, "Accounts");
        var brokenOrders = FirstTableIndex(broken, "Orders");
        var fixedAccounts = FirstTableIndex(fixedVersion, "Accounts");
        var fixedOrders = FirstTableIndex(fixedVersion, "Orders");
        Assert.True(brokenOrders < brokenAccounts);
        Assert.True(fixedAccounts < fixedOrders);
    }

    [Theory]
    [InlineData("10_deadlock_sessionA.sql")]
    [InlineData("11_deadlock_sessionB.sql")]
    [InlineData("20_fixed_sessionA.sql")]
    [InlineData("21_fixed_sessionB.sql")]
    public void Deadlock_repro_scripts_never_set_LOCK_TIMEOUT(string fileName)
    {
        // A lock timeout would fire before SQL Server's own deadlock
        // monitor runs, producing error 1222 (a timeout) instead of a
        // genuine deadlock (error 1205). See README.md. Checked against the
        // executable lines only - the narrative comments in these files
        // legitimately discuss LOCK_TIMEOUT by name to explain why it is
        // absent, which would otherwise false-positive a plain text scan.
        var executable = string.Join("\n", ExecutableLines(fileName));
        Assert.DoesNotMatch(new Regex(@"\bLOCK_TIMEOUT\b", RegexOptions.IgnoreCase), executable);
    }

    [Theory]
    [InlineData("10_deadlock_sessionA.sql")]
    [InlineData("11_deadlock_sessionB.sql")]
    [InlineData("20_fixed_sessionA.sql")]
    [InlineData("21_fixed_sessionB.sql")]
    public void No_deadlock_repro_script_sets_DEADLOCK_PRIORITY_or_changes_isolation_level(string fileName)
    {
        // Checked against executable lines only - see the comment above.
        var executable = string.Join("\n", ExecutableLines(fileName));
        Assert.DoesNotMatch(new Regex(@"DEADLOCK_PRIORITY", RegexOptions.IgnoreCase), executable);
        Assert.DoesNotMatch(new Regex(@"SET\s+TRANSACTION\s+ISOLATION\s+LEVEL", RegexOptions.IgnoreCase), executable);
    }
}
