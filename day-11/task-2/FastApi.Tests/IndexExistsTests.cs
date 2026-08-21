using Microsoft.Data.Sqlite;

namespace FastApi.Tests;

// The core proof for interpretation 5: unlike task-1, which explicitly suppressed the
// convention-created index, task-2 must have it - queried directly from the SQLite
// schema, never assumed from the model configuration alone.
public class IndexExistsTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture = new();

    [Fact]
    public void An_index_covers_Quote_AuthorId()
    {
        using var connection = new SqliteConnection($"Data Source={_fixture.DbPath}");
        connection.Open();

        using var listCommand = connection.CreateCommand();
        listCommand.CommandText = "PRAGMA index_list('Quotes');";

        var indexNames = new List<string>();
        using (var listReader = listCommand.ExecuteReader())
        {
            while (listReader.Read())
            {
                indexNames.Add(listReader.GetString(1));
            }
        }

        var foundAuthorIdIndex = false;
        foreach (var indexName in indexNames)
        {
            using var infoCommand = connection.CreateCommand();
            infoCommand.CommandText = $"PRAGMA index_info('{indexName}');";
            using var infoReader = infoCommand.ExecuteReader();
            while (infoReader.Read())
            {
                if (infoReader.GetString(2) == "AuthorId")
                {
                    foundAuthorIdIndex = true;
                }
            }
        }

        Assert.True(foundAuthorIdIndex, "Expected an index covering Quote.AuthorId, found none.");
    }

    public void Dispose() => _fixture.Dispose();
}
