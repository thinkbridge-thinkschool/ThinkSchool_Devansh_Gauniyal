using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.Logging;

namespace SlowApi;

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
            // EnableSensitiveDataLogging is a development-only switch: it writes real
            // parameter VALUES into the log instead of masking them. Only ever turned on
            // here, per-context, when a collector is actually observing - and every value
            // in this database is synthetic seed data, never real personal data.
            optionsBuilder
                .LogTo(_logCollector.Add, LogLevel.Information)
                .EnableSensitiveDataLogging();
        }
    }

    // EF Core creates an index on any required FK property by convention, via
    // ForeignKeyIndexConvention. Removing that one convention here - rather than trying to
    // remove the index it produces after the fact - is the supported way to suppress it:
    // by the time OnModelCreating's HasForeignKey(...) call returns, the convention has
    // already run and the index already exists, so removing the resulting index there is
    // a no-op (model finalization just recreates it). Confirmed against the created
    // schema in SlowApi.Tests.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Remove(typeof(ForeignKeyIndexConvention));
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

    // WAL mode is stored in the SQLite file header, so setting it once persists across
    // later connections - but this is called on every startup anyway, since re-issuing the
    // pragma is idempotent and cheap. Enabled so concurrent readers (the load test and the
    // single-request diagnostics capture) do not contend on a single writer lock.
    public void EnableWriteAheadLogging()
    {
        Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }
}
