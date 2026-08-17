using Microsoft.Data.Sqlite;

namespace Task2.Tests;

internal static class AuthorQuoteWindowsQuery
{
    public static List<Row> Execute(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = SqlFiles.Read("20_author_quote_windows.sql");
        using var reader = cmd.ExecuteReader();

        var rows = new List<Row>();
        while (reader.Read())
        {
            rows.Add(new Row(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6)));
        }

        return rows;
    }

    internal sealed record Row(
        string AuthorName,
        string QuoteText,
        string CreatedAt,
        int RunningQuoteCount,
        string? PreviousQuoteCreatedAt,
        double? GapDaysRaw,
        double? GapDaysRounded);
}
