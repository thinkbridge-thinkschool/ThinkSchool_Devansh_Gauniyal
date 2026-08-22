using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DapperComparison;

public class QuotesDbContext : DbContext
{
    private readonly string _dataSource;
    private readonly SqlLogCollector? _logCollector;

    public QuotesDbContext(string dataSource, SqlLogCollector? logCollector = null)
    {
        _dataSource = dataSource;
        _logCollector = logCollector;
    }

    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Quote> Quotes => Set<Quote>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dataSource}");

        if (_logCollector is not null)
        {
            optionsBuilder
                .LogTo(_logCollector.Add, LogLevel.Information)
                .EnableSensitiveDataLogging();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Quote>(entity =>
        {
            entity.HasOne(q => q.Author)
                .WithMany(a => a.Quotes)
                .HasForeignKey(q => q.AuthorId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
