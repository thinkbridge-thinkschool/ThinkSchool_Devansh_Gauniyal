using Microsoft.Data.Sqlite;

namespace Task3.Tests;

// A throwaway temp-file SQLite database per test (not :memory: -- matches how run.sh builds
// the real quotes.db), built from the shipped 01_schema.sql + 02_seed.sql. Never touches
// Day 5's or Day 7 Tasks 1/2's database files.
internal sealed class TestDatabase : IDisposable
{
    private readonly string _dbPath;

    public SqliteConnection Connection { get; }

    private TestDatabase(SqliteConnection connection, string dbPath)
    {
        Connection = connection;
        _dbPath = dbPath;
    }

    public static TestDatabase Create()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"day7-task3-tests-{Guid.NewGuid():N}.db");
        var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }

        using (var build = connection.CreateCommand())
        {
            build.CommandText = SqlFiles.Read("01_schema.sql") + "\n" + SqlFiles.Read("02_seed.sql");
            build.ExecuteNonQuery();
        }

        return new TestDatabase(connection, dbPath);
    }

    public void Dispose()
    {
        Connection.Dispose();
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
