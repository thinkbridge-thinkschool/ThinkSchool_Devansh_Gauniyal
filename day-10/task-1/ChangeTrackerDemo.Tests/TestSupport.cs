using ChangeTrackerDemo;

namespace ChangeTrackerDemo.Tests;

// A uniquely-named, throwaway SQLite file per test (or per fixture), deleted on
// Dispose - each test gets full isolation, so no test can observe another test's writes.
public sealed class TemporaryCatalogDatabase : IDisposable
{
    public string Path { get; }

    public TemporaryCatalogDatabase(int rowCount)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"changetrackerdemo-test-{Guid.NewGuid():N}.db");
        using var context = new CatalogContext(Path);
        Seeder.SeedIfNeeded(context, rowCount);
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var f = Path + suffix;
            if (File.Exists(f)) File.Delete(f);
        }
    }
}

// Seeded once per test class via IClassFixture, since a full 10,000-row seed is too
// slow to repeat before every single fact.
public sealed class SharedCatalogFixture : IDisposable
{
    public string DbPath { get; }

    public SharedCatalogFixture()
    {
        DbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"changetrackerdemo-shared-{Guid.NewGuid():N}.db");
        using var context = new CatalogContext(DbPath);
        Seeder.SeedIfNeeded(context, Seeder.RowCount);
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var f = DbPath + suffix;
            if (File.Exists(f)) File.Delete(f);
        }
    }
}
