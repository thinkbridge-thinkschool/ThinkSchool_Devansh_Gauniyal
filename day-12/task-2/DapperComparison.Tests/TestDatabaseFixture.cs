namespace DapperComparison.Tests;

public sealed class TestDatabaseFixture : IDisposable
{
    public string DbPath { get; }

    public TestDatabaseFixture()
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"dappercomparison-test-{Guid.NewGuid():N}.db");

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
