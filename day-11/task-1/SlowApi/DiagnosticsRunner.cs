using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace SlowApi;

// Captures real evidence for one representative request: the full SQL log for a single
// call to the slow endpoint's data-access path, the EXPLAIN QUERY PLAN for its per-author
// query, and a dump of the indexes that actually exist on the Quotes table. Runs as a
// standalone pass (no HTTP involved) so sensitive-data SQL logging is never turned on
// against traffic from the load test itself.
public static class DiagnosticsRunner
{
    public static void Run(string dbPath, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        using (var seedContext = new QuotesDbContext(dbPath))
        {
            seedContext.Database.EnsureCreated();
            seedContext.EnableWriteAheadLogging();
            Seeder.SeedIfNeeded(seedContext);
        }

        var collector = new SqlLogCollector();
        List<AuthorQuoteSummary> summary;
        using (var context = new QuotesDbContext(dbPath, collector))
        {
            summary = AuthorQuoteSummaryQuery.Run(context);
        }

        WriteSqlSample(outputDir, collector, summary.Count);
        WriteQueryPlan(outputDir, dbPath, collector);
        WriteSchemaDump(outputDir, dbPath);
    }

    private static void WriteSqlSample(string outputDir, SqlLogCollector collector, int authorCount)
    {
        var entries = collector.ExecutedCommandEntries;
        var sb = new StringBuilder();
        sb.AppendLine("Single representative request: the in-process call to GET /authors/quote-summary's data-access path.");
        sb.AppendLine($"Authors returned: {authorCount}");
        sb.AppendLine($"Total executed SQL statements captured for this ONE request: {entries.Count}");
        sb.AppendLine($"Expected shape: 1 (load authors) + {authorCount} (one per author) = {authorCount + 1}");
        sb.AppendLine();

        for (int i = 0; i < entries.Count; i++)
        {
            sb.AppendLine($"--- statement {i + 1} of {entries.Count} ---");
            sb.AppendLine(entries[i].Trim());
            sb.AppendLine();
        }

        File.WriteAllText(Path.Combine(outputDir, "sql-sample.log"), sb.ToString());
    }

    private static void WriteQueryPlan(string outputDir, string dbPath, SqlLogCollector collector)
    {
        var entries = collector.ExecutedCommandEntries;
        if (entries.Count < 2)
        {
            throw new InvalidOperationException("Expected at least one per-author query in the captured log.");
        }

        // entries[0] is the "load all authors" query; entries[1] is the first per-author
        // explicit-load query - the one the missing index turns into a full table scan.
        var perAuthorEntry = entries[1];
        var sqlText = ExtractSqlText(perAuthorEntry);
        var parameter = ExtractFirstParameter(perAuthorEntry);

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sqlText;
        if (parameter is not null)
        {
            command.Parameters.AddWithValue(parameter.Value.Name, parameter.Value.Value);
        }

        var sb = new StringBuilder();
        sb.AppendLine("EXPLAIN QUERY PLAN against the exact per-author SQL EF Core generated for this request:");
        sb.AppendLine(sqlText);
        sb.AppendLine();
        sb.AppendLine("Plan output (id | parent | notused | detail):");

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sb.AppendLine($"{reader.GetInt32(0)} | {reader.GetInt32(1)} | {reader.GetInt32(2)} | {reader.GetString(3)}");
        }

        File.WriteAllText(Path.Combine(outputDir, "query-plan.txt"), sb.ToString());
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
        // EnableSensitiveDataLogging prefixes the entry with e.g. Parameters=[@__get_Item_0='1']
        // rather than masking the bound value.
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
