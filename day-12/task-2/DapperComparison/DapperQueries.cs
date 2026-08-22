using Dapper;
using Microsoft.Data.Sqlite;

namespace DapperComparison;

public static class DapperQueries
{
    public const string Sql = @"
SELECT q.Id AS QuoteId, q.Text AS QuoteText, a.Name AS AuthorName, a.Country AS AuthorCountry, q.CreatedAt AS CreatedAt
FROM Quotes q
INNER JOIN Authors a ON q.AuthorId = a.Id
WHERE q.CreatedAt >= @SubmittedSinceUtc
ORDER BY q.CreatedAt DESC, q.Id DESC";

    public static List<QuoteWallItem> Run(string dataSource, DateTime submittedSinceUtc)
    {
        using var connection = new SqliteConnection($"Data Source={dataSource}");
        connection.Open();

        var rows = connection.Query<QuoteWallRow>(Sql, new { SubmittedSinceUtc = submittedSinceUtc });

        return rows.Select(r => new QuoteWallItem
        {
            QuoteId = r.QuoteId,
            QuoteText = r.QuoteText,
            AuthorName = r.AuthorName,
            AuthorCountry = r.AuthorCountry,
            SubmittedOn = r.CreatedAt.ToString("yyyy-MM-dd")
        }).ToList();
    }

    private sealed class QuoteWallRow
    {
        public int QuoteId { get; set; }
        public string QuoteText { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public string AuthorCountry { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
