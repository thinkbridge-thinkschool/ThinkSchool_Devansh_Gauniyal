using Microsoft.Data.Sqlite;

namespace SlowApi.Tests;

// EF Core creates an index on a required FK column by convention. This is the core proof
// that QuotesDbContext really suppressed it - queried directly from the SQLite schema,
// never assumed from the model configuration alone.
public class MissingIndexTests : IDisposable
{
    private readonly TestDatabaseFixture _fixture = new();

    [Fact]
    public void No_index_covers_Quote_AuthorId()
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

        foreach (var indexName in indexNames)
        {
            using var infoCommand = connection.CreateCommand();
            infoCommand.CommandText = $"PRAGMA index_info('{indexName}');";
            using var infoReader = infoCommand.ExecuteReader();
            while (infoReader.Read())
            {
                var columnName = infoReader.GetString(2);
                Assert.NotEqual("AuthorId", columnName);
            }
        }
    }

    public void Dispose() => _fixture.Dispose();
}
