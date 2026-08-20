using Microsoft.EntityFrameworkCore;

namespace ChangeTrackerDemo;

/// <summary>
/// A plain local SQLite file path is passed in directly (e.g. "/tmp/.../catalog.db").
/// This is a filesystem path, not a credentialed connection string - there is no
/// server, user, or password involved, which is why it is safe to construct freely
/// in tests and in the benchmark harness without any secret handling.
/// </summary>
public class CatalogContext : DbContext
{
    private readonly string _dataSource;

    public CatalogContext(string dataSource)
    {
        _dataSource = dataSource;
    }

    public DbSet<Product> Products => Set<Product>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dataSource}");
    }
}
