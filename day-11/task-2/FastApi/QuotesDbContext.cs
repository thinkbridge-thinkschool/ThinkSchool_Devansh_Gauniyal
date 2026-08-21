using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FastApi;

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

    // Task-1 explicitly removed ForeignKeyIndexConvention to suppress the index EF Core
    // creates by convention on a required FK. Task-2 does NOT override
    // ConfigureConventions at all, so that convention runs normally: the index on
    // Quote.AuthorId is created by EF Core's default behaviour, not by an explicit
    // HasIndex(...) call here. Confirmed against the real created schema in
    // FastApi.Tests.IndexExistsTests rather than assumed.
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
    // single-request diagnostics capture) do not contend on a single writer lock, exactly
    // as in task-1.
    public void EnableWriteAheadLogging()
    {
        Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }
}
