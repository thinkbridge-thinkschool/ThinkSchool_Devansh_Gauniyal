using CqrsLite.Data;

namespace CqrsLite.Tests;

// Each test gets its own fresh, uniquely-named SQLite file so tests never share state and
// can run in parallel. Cleans up the main file plus any WAL sidecar files it created.
public sealed class TestDatabaseFixture : IDisposable
{
    public string DbPath { get; }

    public TestDatabaseFixture()
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"cqrslite-test-{Guid.NewGuid():N}.db");

        using var context = new QuotesDbContext(DbPath);
        Seeder.SeedIfNeeded(context);
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var path = DbPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
