using Microsoft.Data.Sqlite;

namespace Task1.Tests;

internal static class AuthorQuoteSummaryQuery
{
    public static List<Row> Execute(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = SqlFiles.Read("20_author_quote_summary.sql");
        using var reader = cmd.ExecuteReader();

        var rows = new List<Row>();
        while (reader.Read())
        {
            rows.Add(new Row(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return rows;
    }

    internal sealed record Row(
        int AuthorId,
        string AuthorName,
        int QuoteCount,
        string? MostRecentQuoteText,
        string? MostRecentQuoteCreatedAt);
}
