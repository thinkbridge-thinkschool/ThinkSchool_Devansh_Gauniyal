# Day 21 Task 1 — HybridCache + stampede protection

This folder is a copy of `day-3/task-3`'s QuotesApi (see `PROVENANCE.md` for exactly
what was copied and why) with HybridCache added in front of its one real database read.
This file is the methodology; `submission.md` is the tight mentor-facing deliverable
list.

## Why a copy, not an edit in place

`day-3/task-3/QuotesApi` cannot be edited: six other tasks in this repo hold a
cross-folder `ProjectReference` straight at it — `day-4/task-2`, `day-4/task-4`,
`day-4/task-5`, `day-4/task-6`, `day-4/task-7`, and `day-11/task-1`. Changing its DI
registrations or startup sequence (which HybridCache registration necessarily does)
would risk breaking all six of those days' test suites. Copying the whole project
(API + its existing test project) into `day-21/task-1` and adding the cache only to the
copy avoids that entirely; `day-17/task-1/api` is an earlier example of the same move in
this repo, for the same reason. `git status --porcelain day-3/task-3` was checked before
and after every step of this task and stayed empty.

## The hot read

`day-3/task-3` exposes two read endpoints. `GET /api/quotes` is backed by an
`InMemoryQuoteRepository` — a plain `Dictionary`, no database at all — so caching in
front of it would have no DB load to drop and would prove nothing. `GET
/api/authors/quote-summary` is the only endpoint that runs a real query: EF Core over a
SQLite `PerformanceDbContext`, deliberately shaped as 1+N (one query for all authors,
then one more per author via explicit `.Collection().Load()`, instead of `.Include()`),
with the FK index convention removed on purpose — the exact N+1 pattern day-11/task-1
already profiled. That's the endpoint this task caches.

## What was added

- `QuotesApi/Caching/DbQueryCounter.cs` — a DI-singleton, `Interlocked`-based counter of
  real DB round trips. Singleton *per app/test host*, not a `static` field: a `static`
  counter would leak between `WebApplicationFactory` instances under xUnit's parallel
  test execution.
- `QuotesApi/Caching/CountingCommandInterceptor.cs` — an EF Core `DbCommandInterceptor`
  that increments the counter on every `ReaderExecuting`/`ReaderExecutingAsync` — a real
  ADO.NET command being sent, not a cache lookup.
- `QuotesApi/Caching/CountingPerformanceDbContext.cs` — a subclass of the carried
  `PerformanceDbContext` that layers the interceptor on top via `OnConfiguring`. A
  subclass rather than an edit, so `Performance/PerformanceDbContext.cs` itself stays
  byte-for-byte what it was in `day-3/task-3`.
- `QuotesApi/Caching/MeasurementOptions.cs` — binds `Measurement:ArtificialDbDelayMs`
  from configuration (default 150ms). Configurable rather than hardcoded, so a miss can
  be made expensive enough for a stampede to be observable without touching code.
- `QuotesApi/Caching/AuthorQuoteSummaryReader.cs` — the one real read path (delay, then
  the query), shared by both the cached and uncached endpoints so the before/after
  comparison in `output/summary.md` is measuring the same query under the same delay.
- `QuotesApi/Caching/AuthorQuoteSummaryCacheService.cs` — wraps
  `HybridCache.GetOrCreateAsync`. Cache key is built in exactly one place
  (`authors:quote-summary:{variant}`) — key construction is the most common source of
  cache bugs, so it isn't inlined per call site.
- `Program.cs` — additive only: HybridCache/Redis registration, and four new endpoints
  (`/api/authors/quote-summary/cached`, `/uncached`, `/api/measurement/db-query-count`,
  `/api/measurement/reset`) appended after the original endpoints, none of which
  changed. A second, independent lazy-seed guard for the new endpoints, so they work
  even if the original endpoint above them is never hit first (see the verification log
  below for a real bug this guard had and how it was fixed).
- `QuotesApi/wwwroot/demo.html` — a single static page, no build step, served by the API
  itself (`app.UseStaticFiles()`), for the local browser demo.
- `QuotesApi.Tests/CachingApiFactory.cs` and `CachingTests.cs` — new tests (see below).
- `scripts/run-measurement.sh` and `scripts/parse-measurement.cs` — the load-test driver
  and percentile parser (see "Measurement" below).

## HybridCache: what was actually verified, not remembered

The task brief names "HybridCache (.NET 9+)" — on .NET 10 here, no version workaround
needed. Rather than writing the wiring from memory, the real API was confirmed by:

1. Adding the packages and letting NuGet resolve real versions: **`Microsoft.Extensions.Caching.Hybrid` 10.9.0** and **`Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.11** (which pulls in `StackExchange.Redis` 2.7.27).
2. Grepping the installed package's XML docs and, for the `HybridCache`/`HybridCacheEntryOptions` abstract types themselves (which ship inside the ASP.NET Core shared framework, not the NuGet package — confirmed by finding them in `Microsoft.Extensions.Caching.Abstractions.dll` under the `Microsoft.AspNetCore.App.Ref` pack), the ref-assembly XML docs — for the real method signatures (`GetOrCreateAsync<TState,T>(key, state, factory, options, tags, cancellationToken)`, `RemoveByTagAsync`, `HybridCacheEntryOptions.Expiration`/`LocalCacheExpiration`).
3. A clean `dotnet build` after writing the wiring against that confirmed surface — the API mismatch this actually caught was `RemoveByTagAsync` returning `ValueTask`, not `Task` (a one-line fix), not anything more serious.

HybridCache's L2 is *any* registered `IDistributedCache` — `AddStackExchangeRedisCache`
registers exactly that, so no separate "tell HybridCache about Redis" call exists; L1
(in-process) is always present, L2 is picked up automatically once registered.

## Expiration values

`Expiration = 30s`, `LocalCacheExpiration = 10s`, set once in `AddHybridCache`'s
`DefaultEntryOptions` (not per-call). Reasoning: the author/quote summary isn't
real-time data, so 30 seconds of staleness in the shared (Redis) tier is an acceptable
trade for cutting real DB load; `LocalCacheExpiration` shorter than `Expiration` (10s vs
30s) means the in-process copy refreshes from Redis more often than the Redis entry
itself expires — in a multi-instance deployment that bounds how long any *one* instance
can serve data that's stale relative to what Redis (and therefore every other instance)
already has, without going all the way back to the database every 10 seconds.

## When Redis is down

Two different behaviours, verified by actually stopping the container (`docker stop
day21-redis`) mid-run, not assumed:

- **Reads** (`GetOrCreateAsync`) degrade to L1-only on their own — this is HybridCache's
  own built-in behaviour, not code written here. A cold miss with Redis down still
  returns 200 and the correct data; it just costs a real Redis connect-timeout on top of
  the artificial DB delay.
- **Tag-based removal** (`RemoveByTagAsync`, used by `/api/measurement/reset`) does
  *not* get the same treatment — it needs the distributed L2 to coordinate invalidation
  across instances, so HybridCache raises the real `StackExchange.Redis
  .RedisConnectionException` instead of swallowing it. This surfaced as a genuine bug
  (see the verification log) and is now caught explicitly in
  `AuthorQuoteSummaryCacheService.EvictAsync`, logged as a warning, and treated as
  non-fatal: the DB-query counter still resets, but any entry already sitting in L2 for
  that tag cannot be proven invalidated until Redis is back or its own `Expiration`
  elapses.

`ConfigurationOptions.AbortOnConnectFail = false` plus explicit 1000ms
`ConnectTimeout`/`SyncTimeout`/`AsyncTimeout` (instead of the client's multi-second
defaults) make that degrade fast rather than making "Redis is down" look like a hang —
measured empirically at ~6s per call with the library defaults, ~0.5–1s with these.
Both behaviours have a dedicated xUnit test (`RedisUnavailable_*` in `CachingTests.cs`),
each pointed at an unreachable `127.0.0.1:1` rather than the shared local container, so
the tests are deterministic regardless of whatever state that container happens to be
in.

## Cache invalidation and staleness

There is no write path in this task (the summary endpoint is read-only), so the only
invalidation this code needs is time-based expiration plus the explicit
`RemoveByTagAsync` used by the demo's reset button and by tests. A real write-behind
system (a quote's data actually changing) would need to call `RemoveByTagAsync` from
whatever write path touches `Authors`/`AuthorQuote` — out of scope here since the
carried `Performance` sub-area has no such write path to hook into.

## Concurrency test design

Concurrency tests in `CachingTests.cs` use a `System.Threading.Barrier`, not
`Thread.Sleep`, to force every caller to dispatch its request at the same instant, and
`Task.Run` (not a bare LINQ `Select` over async lambdas — see the verification log for
why that specifically deadlocked) to guarantee every caller is actually in flight before
any of them reaches the barrier. A wide artificial DB delay (300ms for the stampede
test) makes the coalescing window comfortably larger than any local dispatch jitter.
The one test that legitimately needs a real delay is the expiration test — there's no
way to test a TTL without letting real time pass — and that delay (400ms against a
150ms TTL) is generous specifically to stay robust against machine load rather than race
the clock.

## Measurement — honest limits

The numbers in `output/summary.md` are from one local single-machine run (this laptop,
SQLite, Redis in a local Docker container, bombardier as the load generator, a 150ms
*artificial* DB delay standing in for a genuinely slow query) — they are not production
benchmarks and don't reflect real network latency to a real database or a real Redis, or
what would happen under real multi-instance/multi-region load. What they *do*
demonstrate honestly, on real recorded numbers: the DB-queries/sec gap between the
cached and uncached paths, the cache hit rate once a key is warm, and — via the
dedicated `ConcurrentRequests_SameColdKey_ProduceExactlyOneFactoryRun` xUnit test, which
is the actual stampede-protection proof — that N concurrent misses against a cold key
produce exactly one factory run, not N.

Percentiles are read directly out of bombardier's own computed report using the exact
regex pattern copied from
`day-11/task-1/QuotesApi.Performance.Tests/LatencyPercentileParser.cs` (also present at
`day-11/task-2`) — never estimated, rounded, or hand-typed. See `output/summary.md` for
the actual numbers and `scripts/run-measurement.sh` / `scripts/parse-measurement.cs` for
exactly how they were produced.

## Verification log

Checked carefully for the kind of defect caching code commonly has (key construction,
serialization) and for anything credential-like carried over from the auth-focused
source task. What was actually found:

1. **Real bug — `RemoveByTagAsync` throws when Redis is down.** Stopping the local Redis
   container and then calling `POST /api/measurement/reset` returned a 500 with a raw
   `StackExchange.Redis.RedisConnectionException` from inside
   `DefaultHybridCache.InvalidateL2TagAsync` — unlike a cache *read* miss, which
   HybridCache itself degrades to L1 automatically. Fixed by catching
   `RedisConnectionException` specifically in `AuthorQuoteSummaryCacheService.EvictAsync`
   and logging a warning instead of letting it propagate; the DB-query counter still
   resets. Verified again afterward: `POST /api/measurement/reset` returns 200 with
   Redis stopped, with the warning visible in the server log.
2. **Real bug — a non-deterministic double-checked lock.** The new lazy-seed guard for
   the caching endpoints originally mirrored the *original* endpoint's pattern exactly:
   `if (cachingDbSeeded) return;` *before* acquiring the lock, as a fast-path
   optimization. Under `CachingTests.ConcurrentRequests_SameColdKey_ProduceExactlyOneFactoryRun`
   (40 concurrent first-ever requests against a fresh, unseeded `performance.db`), that
   pre-lock read of a plain (non-`volatile`) `bool` let more than one thread past it
   before the flag's write was visible to the others, so more than one thread reached
   `EnsureCreated()`/`SaveChanges()` and collided with `SQLite Error 1: 'table "Authors"
   already exists'`. Fixed by removing the pre-lock check entirely — every read of the
   flag now happens under the same lock that guards its write, giving it a real
   happens-before relationship. This is a real, if latent, correctness gap in the
   *original* endpoint's identical pattern too (it just hadn't been exercised by 40-way
   genuine concurrency before); the original file was not touched, since it's frozen.
3. **Test-harness bug, not a caching bug — concurrent first `CreateClient()` calls.**
   After fixing #2, the same test still failed deterministically (not flaky - every
   run), now with the SQLite error surfacing from a `SaveChanges()` batch instead of
   `EnsureCreated()`. The actual cause: the test fired 40 concurrent *first-ever*
   `WebApplicationFactory.CreateClient()` calls on one factory, and the framework's lazy
   host-boot isn't safe to trigger from many threads simultaneously — more than one
   independent `Program.cs` boot raced to create/seed the same SQLite file. Fixed by
   calling `CreateClient()` once, synchronously, before the concurrent burst, forcing a
   single safe boot; every subsequent concurrent `CreateClient()` call against an
   already-running host is fine. `ConcurrentRequests_UncachedPath_...` never hit this
   because it happened to already call `CreateClient()` once earlier for its own
   pre-seed step.
4. **Test-only bug — a real deadlock, caught before it shipped.** An earlier version of
   the concurrency tests used `Enumerable.Range(...).Select(async _ => ...)` fed
   straight into `Task.WhenAll`. Because `Select` is lazy, `Task.WhenAll` invoked each
   async lambda synchronously, one at a time, as it enumerated - the first invocation
   blocked forever on `Barrier.SignalAndWait()` before a second lambda ever got a chance
   to run, hanging the whole test run indefinitely (caught by a stuck `dotnet test`,
   not a passing/failing assertion). Fixed by wrapping each iteration in `Task.Run(...)`
   so every caller is dispatched to the thread pool before any of them can block.
5. **Secrets scan:** no credential, connection string, or client secret in any carried
   or new file. `appsettings.json`'s `Entra:TenantId`/`Entra:Audience` (carried
   unchanged from `day-3/task-3`) are Azure AD *directory/application IDs* — not secrets
   — and `Redis:ConnectionString` here is `localhost:6379`, a local-only value with no
   credential (local Redis has none). `AuthApiFactory`/`CachingApiFactory` generate
   random per-run signing keys and passwords via `RandomNumberGenerator`, never a
   literal.
6. **Documentation bug caught in this same file:** an earlier version of the "how to run
   it locally" section below just said `dotnet run`. This project (like `day-3/task-3`,
   which it was copied from) has no `Properties/launchSettings.json`, so `dotnet run`
   without it defaults to the `Production` environment, not `Development` — and
   `Program.cs`'s carried Application Insights/Key Vault code path (untouched, see
   above) requires `KeyVault:Name` outside Development/Testing, so a bare `dotnet run`
   crashes on startup with `KeyVault:Name must be configured...`. Caught by actually
   running the command as written before publishing it. Fixed below by setting
   `ASPNETCORE_ENVIRONMENT=Development` explicitly.

## How to run it locally

```bash
# 1. Start Redis (if not already running)
docker run -d --name day21-redis -p 6379:6379 redis:7.4-alpine
# (if the container already exists: docker start day21-redis)

# 2. Run the API (ASPNETCORE_ENVIRONMENT is required - see verification log point 6;
#    there's no launchSettings.json here, same as the day-3/task-3 original)
cd day-21/task-1/QuotesApi
ASPNETCORE_ENVIRONMENT=Development dotnet run

# 3. Open the demo page (default Kestrel port with no launchSettings.json is 5000)
open http://localhost:5000/demo.html
```

To stop Redis afterward: `docker stop day21-redis` (add `docker rm day21-redis` to
remove the container entirely).

To reproduce the load-test numbers in `output/`:

```bash
cd day-21/task-1
./scripts/run-measurement.sh [concurrency] [duration]   # defaults: 20, 10s
```
