using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Telemetry;

namespace QuotesApi.Tests;

// A throwaway temp-file SQLite database per test (not :memory: -- EF Core's SQLite
// in-memory mode needs a kept-open connection and behaves subtly differently from a
// real file on disk, and a real file is what the app actually uses), wired to its own
// fresh RoundTripCountingInterceptor so each test gets an isolated round-trip count.
internal sealed class TestDatabase : IDisposable
{
    private readonly string _dbPath;

    private TestDatabase(AppDbContext context, RoundTripCountingInterceptor interceptor, string dbPath)
    {
        Context = context;
        Interceptor = interceptor;
        _dbPath = dbPath;
    }

    public AppDbContext Context { get; }
    public RoundTripCountingInterceptor Interceptor { get; }

    public static TestDatabase CreateEmpty()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tracedemo-tests-{Guid.NewGuid():N}.db");
        var interceptor = new RoundTripCountingInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .AddInterceptors(interceptor)
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();

        // Schema creation itself can touch the connection; reset so the count reflects
        // only the query under test, not fixture setup.
        interceptor.Reset();

        return new TestDatabase(context, interceptor, dbPath);
    }

    public static TestDatabase CreateSeeded()
    {
        var db = CreateEmpty();
        SeedData.Seed(db.Context);
        db.Interceptor.Reset();
        return db;
    }

    public void Dispose()
    {
        Context.Dispose();
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
