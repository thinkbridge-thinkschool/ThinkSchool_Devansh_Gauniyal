using Xunit;

namespace Task1.Tests;

public class JoinQueriesTests
{
    [Fact]
    public void InnerJoin_RowCountMatchesTotalQuotes_AndDropsZeroQuoteAuthor()
    {
        using var db = TestDatabase.Create();

        using var countCmd = db.Connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM Quotes;";
        var totalQuotes = Convert.ToInt32(countCmd.ExecuteScalar());

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = SqlFiles.Read("10_inner_join.sql");
        using var reader = cmd.ExecuteReader();

        var rowCount = 0;
        var authorNames = new HashSet<string>();
        while (reader.Read())
        {
            rowCount++;
            authorNames.Add(reader.GetString(0));
        }

        Assert.Equal(totalQuotes, rowCount);
        Assert.DoesNotContain("Confucius", authorNames);
    }

    [Fact]
    public void LeftJoin_SurfacesZeroQuoteAuthor_WithCountColumnMismatch()
    {
        using var db = TestDatabase.Create();

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = SqlFiles.Read("11_left_join.sql");
        using var reader = cmd.ExecuteReader();

        var found = false;
        while (reader.Read())
        {
            if (reader.GetString(1) != "Confucius")
            {
                continue;
            }

            found = true;
            Assert.Equal(1, reader.GetInt32(2)); // RowCount_IncludesNullRow
            Assert.Equal(0, reader.GetInt32(3)); // QuoteCount_TrueCount
        }

        Assert.True(found, "Confucius should be present via the LEFT JOIN despite having zero quotes.");
    }

    [Fact]
    public void CrossJoin_RowCountIsProductOfAuthorsAndTags()
    {
        using var db = TestDatabase.Create();

        using var authorCountCmd = db.Connection.CreateCommand();
        authorCountCmd.CommandText = "SELECT COUNT(*) FROM Authors;";
        var authorCount = Convert.ToInt32(authorCountCmd.ExecuteScalar());

        using var tagCountCmd = db.Connection.CreateCommand();
        tagCountCmd.CommandText = "SELECT COUNT(*) FROM Tags;";
        var tagCount = Convert.ToInt32(tagCountCmd.ExecuteScalar());

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = SqlFiles.Read("12_cross_join.sql");
        using var reader = cmd.ExecuteReader();

        var rowCount = 0;
        var neverUsedRowCount = 0;
        while (reader.Read())
        {
            rowCount++;
            if (reader.GetInt32(2) == 0)
            {
                neverUsedRowCount++;
            }
        }

        Assert.Equal(authorCount * tagCount, rowCount);
        // 'unused-tag' alone contributes one never-used row per author.
        Assert.True(neverUsedRowCount >= authorCount);
    }
}
