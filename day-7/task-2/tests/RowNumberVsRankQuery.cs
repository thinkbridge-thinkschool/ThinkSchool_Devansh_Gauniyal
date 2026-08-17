using Microsoft.Data.Sqlite;

namespace Task2.Tests;

internal static class RowNumberVsRankQuery
{
    public static List<Row> Execute(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = SqlFiles.Read("10_row_number_vs_rank.sql");
        using var reader = cmd.ExecuteReader();

        var rows = new List<Row>();
        while (reader.Read())
        {
            rows.Add(new Row(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5)));
        }

        return rows;
    }

    internal sealed record Row(
        string AuthorName,
        string QuoteText,
        string CreatedAt,
        int RowNum,
        int Rnk,
        int DenseRnk);
}
