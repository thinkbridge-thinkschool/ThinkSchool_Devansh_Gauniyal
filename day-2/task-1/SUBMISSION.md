# Thinkbridge Submission Pack: Day 2, Task 1

## 1. Full `IClock` interface

```csharp
namespace QuotesApi.Services.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

## 2. Full `SystemClock` implementation

```csharp
namespace QuotesApi.Services.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

## 3. Exact DI registration section from `Program.cs`

```csharp
builder.Services.AddDbContext<QuotesDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Quotes")));

builder.Services.AddScoped<IQuoteRepository, QuoteRepository>();
builder.Services.AddTransient<IQuoteValidator, QuoteValidator>();
builder.Services.AddSingleton<IClock, SystemClock>();
```

The explicit registrations are:

```csharp
builder.Services.AddScoped<IQuoteRepository, QuoteRepository>();
builder.Services.AddTransient<IQuoteValidator, QuoteValidator>();
builder.Services.AddSingleton<IClock, SystemClock>();
```

## 4. Full `FakeClock` implementation

```csharp
using QuotesApi.Services.Time;

namespace QuotesApi.Tests.Fakes;

public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; set; }
}
```

## 5. Complete passing fake-clock test

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Tests.Fakes;

namespace QuotesApi.Tests.Repositories;

public class QuoteRepositoryTests
{
    [Fact]
    public async Task CreateAsync_UsesTimeFromInjectedClock()
    {
        var fixedUtcNow = new DateTimeOffset(
            2026, 8, 11, 9, 30, 0, TimeSpan.Zero);
        var fakeClock = new FakeClock(fixedUtcNow);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new QuotesDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var repository = new QuoteRepository(db, fakeClock);

        var createdQuote = await repository.CreateAsync(new Quote
        {
            Author = "Grace Hopper",
            Text = "The most dangerous phrase is: we have always done it this way."
        });

        Assert.Equal(fixedUtcNow, createdQuote.CreatedAtUtc);

        var storedQuote = await db.Quotes.SingleAsync();
        Assert.Equal(fixedUtcNow, storedQuote.CreatedAtUtc);
    }
}
```

This tests real application behavior: `QuoteRepository.CreateAsync` uses the injected time and persists that exact value to SQLite.

## 6. Lifetime mapping

| Lifetime | Registration | Why it fits |
| --- | --- | --- |
| Transient | `IQuoteValidator` to `QuoteValidator` | Small, stateless validation work; actually used by the POST endpoint. |
| Scoped | `IQuoteRepository` to `QuoteRepository` | One repository per request, aligned with the scoped EF Core `QuotesDbContext`. |
| Singleton | `IClock` to `SystemClock` | Stateless and thread-safe, with no request-specific data. |

## 7. Actual verification results

- Restore: succeeded.
- Build: `Build succeeded. 0 Warning(s), 0 Error(s).`
- Test: `Test Run Successful. Total tests: 1. Passed: 1.`
- Test name: `QuotesApi.Tests.Repositories.QuoteRepositoryTests.CreateAsync_UsesTimeFromInjectedClock`.
- Dependency audit: no vulnerable packages after selecting patched `SQLitePCLRaw.lib.e_sqlite3` 2.1.12.
- API startup: both migrations applied to a fresh temporary SQLite database; Kestrel listened on `http://127.0.0.1:5087`; no DI errors.
- Manual API checks: health 200, invalid create 400, valid create 201 with `createdAtUtc`, list 200 with the persisted timestamp.
- Shutdown: clean, exit code 0.

## 8. Git and GitHub

- Branch: `day-2/task-1`
- Task commit: `761b736d1c9eabedcb1b268355c27c5f01692651`
- GitHub link: **PENDING PUBLICATION** — after pushing, use `https://github.com/devansh-gauniyal/thinkschool/tree/761b736d1c9eabedcb1b268355c27c5f01692651/day-2/task-1`

If publication is still pending, run from the repository root:

```bash
git push -u origin day-2/task-1
```

The commit link above becomes available as soon as that push succeeds.

## 9. Ready-to-paste form answers

### Mentor notes

Added all three DI lifetimes and a testable UTC clock. New quotes are timestamped through `IClock`, and the SQLite-backed fake-clock test proves the exact fixed value is persisted.

### What did you learn this session?

I learned how DI lifetimes control how long services live, and how constructor-injecting a clock makes time-dependent behavior deterministic and testable.

### What would break this?

Injecting the scoped repository or `DbContext` into a singleton could share request state incorrectly. Mutable state in `SystemClock` could also introduce concurrency bugs.

## 10. Short interview explanation

The API uses a transient validator for lightweight stateless work, a scoped repository and `DbContext` for one request, and a singleton clock because it is stateless and thread-safe. The repository receives `IClock` through its constructor and timestamps new quotes. In tests I replace the real clock with a fixed fake, so I can assert the exact stored time without sleeps or timing ranges.

## 11. Important commands used

```bash
dotnet restore Task1.slnx
dotnet ef migrations add AddQuoteCreatedAtUtc --project QuotesApi/QuotesApi.csproj --startup-project QuotesApi/QuotesApi.csproj --output-dir Migrations
dotnet build Task1.slnx --no-restore
dotnet test Task1.slnx --no-build --no-restore --logger 'console;verbosity=normal'
dotnet list QuotesApi/QuotesApi.csproj package --vulnerable --include-transitive
ConnectionStrings__Quotes='Data Source=/private/tmp/quotes-api-day2-final.mn6N9i/quotes.db' dotnet run --no-build --project QuotesApi/QuotesApi.csproj --urls http://127.0.0.1:5087
curl http://127.0.0.1:5087/
curl -X POST http://127.0.0.1:5087/api/quotes -H 'Content-Type: application/json' -d '{"author":"Grace Hopper","text":"A ship in port is safe."}'
curl 'http://127.0.0.1:5087/api/quotes?page=1&size=10'
```
