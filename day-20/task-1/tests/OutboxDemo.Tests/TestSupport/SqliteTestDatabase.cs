using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OutboxDemo.Data;

namespace OutboxDemo.Tests.TestSupport;

/// <summary>
/// A real, file-based SQLite database under this test project's own bin
/// output (never outside day-20/task-1), migrated once at construction.
/// Connections are opened explicitly and kept open so a PRAGMA (busy
/// timeout) applies for the life of the context, which the concurrency
/// tests rely on.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly string _dbPath;

    public string ConnectionString { get; }

    public SqliteTestDatabase()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "test-dbs");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, $"outbox-test-{Guid.NewGuid():N}.db");
        ConnectionString = $"Data Source={_dbPath}";

        using var db = CreateContext();
        db.Database.Migrate();
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(ConnectionString)
            .Options;

        var context = new AppDbContext(options);
        context.Database.OpenConnection();
        context.Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");
        return context;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
