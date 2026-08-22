using System.Text;
using System.Text.Json;

namespace DapperComparison;

public static class EvidenceRunner
{
    public static void Run(string dbPath, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteIfExists(dbPath);
        DeleteIfExists(dbPath + "-shm");
        DeleteIfExists(dbPath + "-wal");

        using (var seedContext = new QuotesDbContext(dbPath))
        {
            Seeder.SeedIfNeeded(seedContext);
        }

        var results = Comparison.Run(dbPath);
        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outputDir, "results.json"), json);

        CaptureEfSql(dbPath, outputDir);
        CaptureDapperSql(outputDir);
    }

    private static void CaptureEfSql(string dbPath, string outputDir)
    {
        var trackedCollector = new SqlLogCollector();
        using (var context = new QuotesDbContext(dbPath, trackedCollector))
        {
            EfQueries.RunTracked(context, Comparison.SubmittedSinceUtc);
        }
        WriteSqlLog(outputDir, "ef-tracked-sql.log", "EfQueries.RunTracked - tracked entities via Include (the unfair baseline)", trackedCollector);

        var projectionCollector = new SqlLogCollector();
        using (var context = new QuotesDbContext(dbPath, projectionCollector))
        {
            EfQueries.RunProjection(context, Comparison.SubmittedSinceUtc);
        }
        WriteSqlLog(outputDir, "ef-projection-sql.log", "EfQueries.RunProjection - AsNoTracking projection straight into QuoteWallItem (the fair baseline)", projectionCollector);
    }

    private static void CaptureDapperSql(string outputDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DapperQueries.Sql - the literal SQL string executed via Dapper, parameterised with @SubmittedSinceUtc.");
        sb.AppendLine($"Parameter value used for every measured run: {Comparison.SubmittedSinceUtc:O}");
        sb.AppendLine();
        sb.AppendLine(DapperQueries.Sql.Trim());
        File.WriteAllText(Path.Combine(outputDir, "dapper-sql.log"), sb.ToString());
    }

    private static void WriteSqlLog(string outputDir, string fileName, string header, SqlLogCollector collector)
    {
        var entries = collector.ExecutedCommandEntries;
        var sb = new StringBuilder();
        sb.AppendLine(header);
        sb.AppendLine($"Total executed SQL statements captured: {entries.Count}");
        sb.AppendLine();

        for (int i = 0; i < entries.Count; i++)
        {
            sb.AppendLine($"--- statement {i + 1} of {entries.Count} ---");
            sb.AppendLine(entries[i].Trim());
            sb.AppendLine();
        }

        File.WriteAllText(Path.Combine(outputDir, fileName), sb.ToString());
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
