# Day 2, Task 1: Dependency Injection at Depth

## What this task builds

This task extends the existing Quotes API to demonstrate all three dependency-injection (DI) lifetimes. New quotes now receive a UTC creation timestamp, but application code gets the time through an `IClock` abstraction instead of reading the computer clock directly. This makes the behavior easy to test with a fixed time.

The Day 1 source was copied without its generated `bin`, `obj`, or SQLite database files. The Day 2 API builds a clean local database from the retained EF Core migrations when it starts. Local database files are ignored by this task's `.gitignore`.

## Dependency injection in plain language

Dependency injection means a class declares the collaborators it needs, and the framework supplies them. For example, `QuoteRepository` asks for `QuotesDbContext` and `IClock` in its constructor. It does not create either dependency itself. This keeps construction in one place and lets tests supply controlled alternatives.

## The three lifetimes

| Lifetime | Service in this project | Meaning and reason |
| --- | --- | --- |
| Transient | `IQuoteValidator` / `QuoteValidator` | A new validator is created whenever it is resolved. Validation is lightweight, stateless work. |
| Scoped | `IQuoteRepository` / `QuoteRepository` | One repository is used per HTTP request. It shares the request's scoped `QuotesDbContext`, which `AddDbContext` also registers as scoped by default. |
| Singleton | `IClock` / `SystemClock` | One stateless, thread-safe clock serves the application lifetime. It has no request-specific or mutable state. |

A singleton must not capture a scoped repository or `DbContext`. Doing so could share request-specific state across requests and cause concurrency or data-tracking bugs.

## Constructor injection and the clock

Constructor injection puts required dependencies in a class constructor. `QuoteRepository` receives an `IClock` this way and sets `CreatedAtUtc` when it creates a quote.

A direct `DateTimeOffset.UtcNow` call is hard to test because the expected value changes continuously. `IClock` separates "get the current UTC time" from the system implementation. Production uses `SystemClock`; the test injects `FakeClock` with a fixed `DateTimeOffset`. The assertion is exact, deterministic, and does not need delays or time ranges.

## Important files

- `QuotesApi/Services/Time/IClock.cs`: clock contract.
- `QuotesApi/Services/Time/SystemClock.cs`: the only production code that reads the real system clock.
- `QuotesApi/Services/QuoteValidator.cs`: transient, stateless quote validation.
- `QuotesApi/Repositories/QuoteRepository.cs`: scoped persistence and creation timestamp behavior.
- `QuotesApi/Models/Quote.cs`: includes `CreatedAtUtc` as `DateTimeOffset`.
- `QuotesApi/Migrations/20260811040557_AddQuoteCreatedAtUtc.cs`: Day 2-only schema change.
- `QuotesApi/Program.cs`: DI registrations and endpoints.
- `QuotesApi.Tests/Fakes/FakeClock.cs`: controllable test clock.
- `QuotesApi.Tests/Repositories/QuoteRepositoryTests.cs`: deterministic clock test using SQLite in memory.
- `SUBMISSION.md`: ready-to-paste Thinkbridge submission material.

## Build and run

From `day-2/task-1`:

```bash
dotnet restore Task1.slnx
dotnet build Task1.slnx --no-restore
dotnet test Task1.slnx --no-build --no-restore
dotnet run --project QuotesApi/QuotesApi.csproj
```

The default connection string is `Data Source=quotes.db`. EF Core applies the included migrations at startup. To keep a verification database elsewhere, override it without changing source:

```bash
ConnectionStrings__Quotes='Data Source=/tmp/quotes.db' \
  dotnet run --project QuotesApi/QuotesApi.csproj
```

## Verified result

Verified on August 11, 2026 with .NET SDK 10.0.302:

- Restore: succeeded; the dependency audit reported no vulnerable packages.
- Build: succeeded with 0 warnings and 0 errors.
- Tests: 1 passed, 0 failed. `CreateAsync_UsesTimeFromInjectedClock` passed.
- Startup: both migrations applied to a fresh temporary SQLite database, Kestrel listened on `http://127.0.0.1:5087`, and there were no DI errors.
- `GET /`: 200 with `Quotes API is running`.
- Invalid `POST /api/quotes`: 400 with the existing author/text validation message.
- Valid `POST /api/quotes`: 201 with a non-default `createdAtUtc` UTC offset value.
- `GET /api/quotes?page=1&size=10`: 200 and returned the persisted quote with the same timestamp.
- Shutdown: clean, exit code 0.

The copied EF Core dependency initially resolved a vulnerable transitive native SQLite package (`2.1.11`). A direct compatible reference to patched version `2.1.12` removes that warning; the final vulnerability audit is clean.
