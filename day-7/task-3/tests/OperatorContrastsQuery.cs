using Microsoft.Data.Sqlite;

namespace Task3.Tests;

// Executes the shipped sql/20_operator_contrasts.sql as one multi-statement batch and
// walks its result sets via NextResult() to pull out the specific comparisons the tests
// need -- this avoids re-typing any of that file's SQL as a C# string literal.
//
// Statement order in the file (0-based, must match the file's actual sequence):
//   0 UNION count            1 UNION ALL count
//   2 EXCEPT (Q1 authors)    3 LEFT JOIN + HAVING (Q1 equivalent)
//   4 INTERSECT (Q2 authors) 5 duplicated-join-row-count demo
//   6 EXCEPT NULL demo       7 INTERSECT NULL demo        8 plain NULL = NULL demo
internal static class OperatorContrastsQuery
{
    public static Result Execute(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = SqlFiles.Read("20_operator_contrasts.sql");
        using var reader = cmd.ExecuteReader();

        var unionCount = ReadSingleInt(reader);
        reader.NextResult();
        var unionAllCount = ReadSingleInt(reader);

        reader.NextResult();
        var exceptAuthorNames = ReadStringColumn(reader);
        reader.NextResult();
        var leftJoinAuthorNames = ReadStringColumn(reader);

        reader.NextResult();
        var intersectAuthorNames = ReadStringColumn(reader);

        return new Result(unionCount, unionAllCount, exceptAuthorNames, leftJoinAuthorNames, intersectAuthorNames);
    }

    private static int ReadSingleInt(SqliteDataReader reader)
    {
        reader.Read();
        return reader.GetInt32(1); // column 0 is the label, column 1 is the count
    }

    private static List<string> ReadStringColumn(SqliteDataReader reader)
    {
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }
        return values;
    }

    internal sealed record Result(
        int UnionCount,
        int UnionAllCount,
        List<string> ExceptAuthorNames,
        List<string> LeftJoinAuthorNames,
        List<string> IntersectAuthorNames);
}
