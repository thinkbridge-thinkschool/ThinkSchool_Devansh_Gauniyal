using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace FastApi;

// Captures real evidence for one representative request against a named fixed variant:
// the full SQL log, the EXPLAIN QUERY PLAN for every statement that request executed, and
// a dump of the indexes that actually exist on the Quotes table. Runs as a standalone pass
// (no web host) so sensitive-data SQL logging never runs against load-test traffic.
public static class DiagnosticsRunner
{
    public static void Run(string variant, string dbPath, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        using (var seedContext = new QuotesDbContext(dbPath))
        {
            seedContext.Database.EnsureCreated();
            seedContext.EnableWriteAheadLogging();
            Seeder.SeedIfNeeded(seedContext);
        }

        var collector = new SqlLogCollector();
        List<AuthorWithQuotesDto> result;
        using (var context = new QuotesDbContext(dbPath, collector))
        {
            result = variant switch
            {
                "projection" => Queries.RunProjection(context),
                "split" => Queries.RunSplitQuery(context),
                "slow" => Queries.RunSlow(context),
                _ => throw new ArgumentException($"Unknown variant '{variant}'.")
            };
        }

        WriteSqlSample(outputDir, variant, collector, result.Count);
        WriteQueryPlan(outputDir, variant, dbPath, collector);
        WriteSchemaDump(outputDir, dbPath);
    }

    private static void WriteSqlSample(string outputDir, string variant, SqlLogCollector collector, int authorCount)
    {
        var entries = collector.ExecutedCommandEntries;
        var sb = new StringBuilder();
        sb.AppendLine($"Single representative request: the in-process call to the '{variant}' endpoint's data-access path.");
        sb.AppendLine($"Authors returned: {authorCount}");
        sb.AppendLine($"Total executed SQL statements captured for this ONE request: {entries.Count}");
        sb.AppendLine();

        for (int i = 0; i < entries.Count; i++)
        {
            sb.AppendLine($"--- statement {i + 1} of {entries.Count} ---");
            sb.AppendLine(entries[i].Trim());
            sb.AppendLine();
        }

        File.WriteAllText(Path.Combine(outputDir, $"sql-sample-{variant}.log"), sb.ToString());
    }

    private static void WriteQueryPlan(string outputDir, string variant, string dbPath, SqlLogCollector collector)
    {
        var entries = collector.ExecutedCommandEntries;
        if (entries.Count == 0)
        {
            throw new InvalidOperationException("Expected at least one executed statement to capture a plan for.");
        }

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        var sb = new StringBuilder();
        sb.AppendLine($"EXPLAIN QUERY PLAN for every statement the '{variant}' endpoint executed for this request ({entries.Count} statement(s)):");
        sb.AppendLine();

        for (int i = 0; i < entries.Count; i++)
        {
            var sqlText = ExtractSqlText(entries[i]);
            var parameter = ExtractFirstParameter(entries[i]);

            using var command = connection.CreateCommand();
            command.CommandText = "EXPLAIN QUERY PLAN " + sqlText;
            if (parameter is not null)
            {
                command.Parameters.AddWithValue(parameter.Value.Name, parameter.Value.Value);
            }

            sb.AppendLine($"--- statement {i + 1} of {entries.Count} ---");
            sb.AppendLine(sqlText);
            sb.AppendLine();
            sb.AppendLine("Plan output (id | parent | notused | detail):");

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                sb.AppendLine($"{reader.GetInt32(0)} | {reader.GetInt32(1)} | {reader.GetInt32(2)} | {reader.GetString(3)}");
            }

            sb.AppendLine();
        }

        File.WriteAllText(Path.Combine(outputDir, $"query-plan-{variant}.txt"), sb.ToString());
    }

    private static void WriteSchemaDump(string outputDir, string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        var sb = new StringBuilder();
        sb.AppendLine("CREATE TABLE statement for Quotes:");
        using (var tableCmd = connection.CreateCommand())
        {
            tableCmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='Quotes';";
            sb.AppendLine((tableCmd.ExecuteScalar() as string) ?? "(not found)");
        }

        sb.AppendLine();
        sb.AppendLine("Indexes that exist on the Quotes table (name | sql):");
        using (var indexCmd = connection.CreateCommand())
        {
            indexCmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='index' AND tbl_name='Quotes';";
            using var reader = indexCmd.ExecuteReader();
            bool any = false;
            while (reader.Read())
            {
                any = true;
                var name = reader.GetString(0);
                var sql = reader.IsDBNull(1) ? "(no SQL text - implicit index, e.g. backing the PRIMARY KEY)" : reader.GetString(1);
                sb.AppendLine($"{name} | {sql}");
            }

            if (!any)
            {
                sb.AppendLine("(no indexes found on the Quotes table)");
            }
        }

        File.WriteAllText(Path.Combine(outputDir, "schema-dump.txt"), sb.ToString());
    }

    private static string ExtractSqlText(string logEntry)
    {
        int selectIndex = logEntry.IndexOf("SELECT", StringComparison.Ordinal);
        if (selectIndex < 0)
        {
            throw new InvalidOperationException("Could not locate SQL text in captured log entry.");
        }

        return logEntry[selectIndex..].Trim();
    }

    private static (string Name, object Value)? ExtractFirstParameter(string logEntry)
    {
        var match = Regex.Match(logEntry, @"(@\w+)='([^']*)'");
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups[1].Value;
        var rawValue = match.Groups[2].Value;
        object value = int.TryParse(rawValue, out var intValue) ? intValue : rawValue;
        return (name, value);
    }
}
