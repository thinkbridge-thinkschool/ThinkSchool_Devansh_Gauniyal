using System.Text.RegularExpressions;
using Xunit;

namespace Day9Task1.Verification;

public class SqlArtifactTests
{
    public static readonly string[] ExpectedSqlFiles =
    [
        "00_create_database.sql",
        "01_schema.sql",
        "02_seed.sql",
        "03_verify_snapshot_off.sql",
        "10_dirty_read_sessionA.sql",
        "10_dirty_read_sessionB.sql",
        "11_nonrepeatable_sessionA.sql",
        "11_nonrepeatable_sessionB.sql",
        "12_phantom_sessionA.sql",
        "12_phantom_sessionB.sql",
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

    public static readonly string[] Anomalies = ["10_dirty_read", "11_nonrepeatable", "12_phantom"];

    public static IEnumerable<object[]> AnomalyNames() => Anomalies.Select(a => new object[] { a });

    [Theory]
    [MemberData(nameof(AnomalyNames))]
    public void Each_anomaly_has_both_a_session_A_and_session_B_script_with_an_explicit_isolation_level(string anomaly)
    {
        var sessionA = File.ReadAllText(Paths.Sql($"{anomaly}_sessionA.sql"));
        var sessionB = File.ReadAllText(Paths.Sql($"{anomaly}_sessionB.sql"));

        Assert.False(string.IsNullOrWhiteSpace(sessionA));
        Assert.False(string.IsNullOrWhiteSpace(sessionB));

        // Only Session B's isolation level is the parameter under test in
        // every anomaly here (see README.md) - Session B's script carries the
        // placeholder and the explicit SET TRANSACTION ISOLATION LEVEL
        // statement.
        Assert.Matches(new Regex(@"SET\s+TRANSACTION\s+ISOLATION\s+LEVEL\s+__ISOLATION_LEVEL__", RegexOptions.IgnoreCase), sessionB);
    }

    [Fact]
    public void Phantom_scripts_use_an_INSERT_to_create_the_phantom_not_an_UPDATE()
    {
        var sessionA = File.ReadAllText(Paths.Sql("12_phantom_sessionA.sql"));

        Assert.Matches(new Regex(@"\bINSERT\s+INTO\s+dbo\.Accounts", RegexOptions.IgnoreCase), sessionA);
        Assert.DoesNotMatch(new Regex(@"\bUPDATE\s+dbo\.Accounts", RegexOptions.IgnoreCase), sessionA);
    }

    public static readonly (string Anomaly, string OccursTag, string PreventedTag)[] RunPairs =
    [
        ("10_dirty_read", "occurs_READ_UNCOMMITTED", "prevented_READ_COMMITTED"),
        ("11_nonrepeatable", "occurs_READ_COMMITTED", "prevented_REPEATABLE_READ"),
        ("12_phantom", "occurs_REPEATABLE_READ", "prevented_SERIALIZABLE"),
    ];

    public static IEnumerable<object[]> RunPairData() => RunPairs.Select(p => new object[] { p.Anomaly, p.OccursTag, p.PreventedTag });

    private static readonly string[] KnownLevels = ["READ UNCOMMITTED", "READ COMMITTED", "REPEATABLE READ", "SERIALIZABLE"];

    private static string NeutralizeIsolationLevel(string text)
    {
        foreach (var level in KnownLevels)
            text = text.Replace(level, "__LEVEL__");
        return text;
    }

    [Theory]
    [MemberData(nameof(RunPairData))]
    public void Occurring_and_preventing_runs_differ_only_in_the_isolation_level(string anomaly, string occursTag, string preventedTag)
    {
        // The rendered copies in output/ are what the orchestrator actually
        // executed for each run (see scripts/run-experiment.sh: open_run
        // copies sessionA.sql verbatim and sed-substitutes sessionB.sql).
        // Comparing those - after neutralizing every occurrence of a known
        // isolation-level name - catches any silent extra difference between
        // the two runs.
        foreach (var session in new[] { "sessionA", "sessionB" })
        {
            var occursPath = Paths.Rendered(anomaly, occursTag, session);
            var preventedPath = Paths.Rendered(anomaly, preventedTag, session);

            Assert.True(File.Exists(occursPath), $"Missing rendered script: {occursPath}");
            Assert.True(File.Exists(preventedPath), $"Missing rendered script: {preventedPath}");

            var occursText = NeutralizeIsolationLevel(File.ReadAllText(occursPath));
            var preventedText = NeutralizeIsolationLevel(File.ReadAllText(preventedPath));

            Assert.Equal(occursText, preventedText);
        }
    }
}
