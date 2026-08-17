using Microsoft.Data.Sqlite;

namespace Task3.Tests;

// Shared reader for graded queries that return a single text column: AuthorName from
// 10_q1_authors_with_quotes_no_tags.sql / 11_q2_authors_in_both_sets.sql, or TagName from
// 12_q3_combined_distinct_tags.sql.
internal static class SingleColumnQuery
{
    public static List<string> Execute(SqliteConnection connection, string sqlFileName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = SqlFiles.Read(sqlFileName);
        using var reader = cmd.ExecuteReader();

        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
