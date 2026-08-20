using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace QueryTranslationDemo;

public class CatalogContext : DbContext
{
    private readonly string _dataSource;
    private readonly SqlLogCollector? _logCollector;

    public CatalogContext(string dataSource, SqlLogCollector? logCollector = null)
    {
        _dataSource = dataSource;
        _logCollector = logCollector;
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dataSource}");

        if (_logCollector is not null)
        {
            // EnableSensitiveDataLogging is a development-only switch: it writes real
            // parameter VALUES into the log instead of masking them. Never enable this
            // in production - it can put personal data straight into your logs. It is
            // only turned on here, per-context, when a collector is actually observing.
            optionsBuilder
                .LogTo(_logCollector.Add, LogLevel.Information)
                .EnableSensitiveDataLogging();
        }
    }
}
