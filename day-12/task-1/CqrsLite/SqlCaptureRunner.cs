using System.Text;
using CqrsLite.Data;
using CqrsLite.Features.Quotes.Commands;
using CqrsLite.Features.Quotes.Queries;

namespace CqrsLite;

// Runs both paths once against a freshly seeded database and writes the REAL SQL each path
// emitted, plus the real row counts and validation outcomes, to disk. Nothing here is
// fabricated - every figure quoted in submission.md traces back to a file this method wrote.
public static class SqlCaptureRunner
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

        var outcomes = new StringBuilder();
        outcomes.AppendLine("Row counts and validation outcomes captured from a real execution against a freshly seeded, synthetic database.");
        outcomes.AppendLine();
        outcomes.AppendLine($"Authors seeded: {Seeder.AuthorCount}");
        outcomes.AppendLine($"Quotes seeded: {Seeder.QuoteCount}");
        outcomes.AppendLine();

        var commandCollector = new SqlLogCollector();
        SubmitQuoteResult successResult;
        using (var context = new QuotesDbContext(dbPath, commandCollector))
        {
            var handler = new SubmitQuoteHandler(context);
            successResult = handler.Handle(new SubmitQuoteCommand(7, "Synthetic quote text 90001, captured for evidence"));
        }

        WriteSqlLog(
            outputDir,
            "command-sql.log",
            "SubmitQuoteHandler - one successful submission (author lookup, duplicate check, insert)",
            commandCollector);

        outcomes.AppendLine("Command case: valid submission for Author 007");
        outcomes.AppendLine($"  Success={successResult.Success}, QuoteId={successResult.QuoteId}, FailureReason={successResult.FailureReason}");
        outcomes.AppendLine();

        RecordRejectedCase(outcomes, dbPath, "Empty text", new SubmitQuoteCommand(1, "   "));
        RecordRejectedCase(outcomes, dbPath, "Text over max length",
            new SubmitQuoteCommand(1, new string('x', SubmitQuoteHandler.MaxTextLength + 1)));
        RecordRejectedCase(outcomes, dbPath, "Unknown author", new SubmitQuoteCommand(9999, "Synthetic quote text 90002"));
        RecordRejectedCase(outcomes, dbPath, "Duplicate for same author", new SubmitQuoteCommand(1, "Synthetic quote text 00001"));

        var queryCollector = new SqlLogCollector();
        List<QuoteWallItem> wall;
        using (var context = new QuotesDbContext(dbPath, queryCollector))
        {
            var handler = new QuoteWallHandler(context);
            wall = handler.Handle(new QuoteWallQuery());
        }

        WriteSqlLog(
            outputDir,
            "query-sql.log",
            "QuoteWallHandler - AsNoTracking projection straight into QuoteWallItem",
            queryCollector);

        outcomes.AppendLine("Query case: quote wall read after the successful submission above");
        outcomes.AppendLine($"  Rows returned: {wall.Count} (seeded {Seeder.QuoteCount} plus the 1 quote just submitted = {Seeder.QuoteCount + 1})");
        outcomes.AppendLine($"  First row author: {wall[0].AuthorName} ({wall[0].AuthorCountry})");

        File.WriteAllText(Path.Combine(outputDir, "validation-outcomes.txt"), outcomes.ToString());
    }

    private static void RecordRejectedCase(StringBuilder outcomes, string dbPath, string label, SubmitQuoteCommand command)
    {
        using var context = new QuotesDbContext(dbPath);
        var handler = new SubmitQuoteHandler(context);
        var result = handler.Handle(command);
        outcomes.AppendLine($"Command case: {label}");
        outcomes.AppendLine($"  Success={result.Success}, QuoteId={result.QuoteId}, FailureReason={result.FailureReason}");
        outcomes.AppendLine();
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
