# Day 22 Task 1 — Resilience with Polly

This folder is a copy-forward of `day-21/task-1` (itself a copy of `day-3/task-3`;
see `PROVENANCE.md`) with a Polly v8 resilience layer added in front of both of the
app's outbound dependencies: Redis (already present from Day 21) and a new,
controllable HTTP dependency. This file is the methodology; `submission.md` is the
tight mentor-facing deliverable list.

## Why a copy, not an edit in place

Same reasoning Day 21 already established, one level deeper: `day-3/task-3/QuotesApi`
cannot be edited (six other tasks hold a `ProjectReference` straight at it), so Day 21
copied it. Day 22 copies `day-21/task-1` in turn rather than editing it, for a
narrower reason — this task adds real behavioural changes to the Redis registration
(wrapping `IDistributedCache` in a resilience decorator) and to
`AuthorQuoteSummaryCacheService.EvictAsync` (a second exception type to catch), and
Day 21's own branch is merged and should stay exactly what shipped. `git status
--porcelain day-21/task-1` and `day-3/task-3` were checked empty before, during, and
after every step of this task.

## The two dependencies, and why they get different pipelines

**Redis** (Day 21's `IDistributedCache`, wrapping `StackExchange.Redis`) gets
**timeout, circuit breaker, bulkhead — no retry.** **The controllable HTTP
dependency** (`GET /api/external/quote-of-the-day`, new this task) gets the full
**bulkhead → retry → circuit breaker → timeout** pipeline.

The reason retry is on one and not the other: retrying a cache *read* is not what you
would do in production. If Redis is failing, the correct move is to skip the cache
entirely and go straight to the source of truth — which is exactly what already
happens here, because HybridCache's `GetOrCreateAsync` (Day 21, untouched) falls back
to its factory (a real database read) the moment the L2 call fails, retried or not.
Retrying the Redis call first would just add latency in front of that fallback for no
benefit. Retry only makes sense for an **idempotent** operation where succeeding on
attempt two is strictly better than giving up on attempt one — a plain `GET` with no
side effects, which is what the external-service endpoint is and what the cache write
path is not. This is also literally what the brief's "(idempotent only)" parenthetical
is pointing at.

`ExternalServiceClient.GetQuoteOfTheDayAsync` (`QuotesApi/Resilience/
ExternalServiceClient.cs`) is the only caller of the retry-enabled named HttpClient,
and it only ever issues a `GET`. The Redis pipeline (`RedisResiliencePipelineConfiguration.cs`)
has no `AddRetry` call at all — not disabled, not configured with zero attempts,
simply never added to that pipeline's builder.

## The four strategies, and the failure each addresses

- **Timeout** — bounds how long a single call is allowed to take, so a dependency
  that's hanging (not erroring, just never responding) can't hold a caller forever.
  Addresses: a slow or stuck dependency.
- **Retry (with exponential backoff + jitter)** — re-attempts a failed call a bounded
  number of times, waiting longer between each attempt, with randomness so many
  callers don't all retry in lockstep and recreate the exact spike that just failed.
  Addresses: a transient failure that's likely to succeed a moment later (a brief
  503, a dropped connection).
- **Circuit breaker** — after enough recent calls have failed, stops even trying for
  a cooldown period, failing fast instead. Addresses: a dependency that's genuinely
  down or overloaded, where continuing to hammer it makes recovery slower, not faster.
- **Bulkhead (concurrency limiter)** — caps how many calls to a dependency can be in
  flight at once, queueing a few more before rejecting the rest outright. Addresses:
  one dependency (or one slow spot) consuming so many of the caller's own resources
  (threads, connections) that unrelated work in the same app starts failing too.

## Why strategy order matters, and what this task's order means

Order is not cosmetic. In Polly v8, the first strategy added to a
`ResiliencePipelineBuilder` is the **outermost** wrapper; the last is **innermost**,
closest to the real call. day-5/task-6/ResilienceDemo already established this for
its 3-strategy pipeline (retry → circuit breaker → timeout); this task's own
reasoning, worked out independently rather than copied, for the fourth strategy:

**Chosen order, outermost to innermost: Bulkhead → Retry → Circuit breaker → Timeout**
(HTTP dependency; Redis drops retry: **Bulkhead → Circuit breaker → Timeout**).

- **Bulkhead outermost.** It caps how many *logical calls* to a dependency are
  in flight across the whole app at once. If it sat inside retry instead, each retry
  attempt of the *same* logical call would compete for its own fresh permit, and the
  count would stop meaning "how many calls are outstanding" and start meaning "how
  many attempts are outstanding" — the wrong thing to limit, and it would also mean a
  slow attempt's backoff *wait* wouldn't release its permit, wasting capacity on a
  caller that isn't even using the dependency right now. This also matches
  `Microsoft.Extensions.Http.Resilience`'s own `AddStandardResilienceHandler` (same
  package, version 10.9.0), whose documented default order places its rate limiter
  outermost, ahead of retry.
- **Retry next.** It governs the whole attempt sequence for one logical call.
- **Circuit breaker inside retry.** Every retry attempt passes through the breaker
  check first, so once the circuit opens, further retries fail fast instead of still
  trying — the same reasoning day-5/task-6 already established.
- **Timeout innermost.** It bounds a *single attempt*, not the whole retry sequence.
  If timeout were outermost, one retry sequence's total wall-clock time would be
  capped as a single unit, and a slow first attempt could consume the entire budget
  before a retry even got a chance to run. Innermost, each attempt gets its own full
  timeout budget regardless of how many attempts came before it.

## The circuit breaker's state machine, and every configured value

States: **Closed** (calls flow through normally, failures are counted) →
**Open** (every call fails fast without touching the dependency, for `BreakDuration`)
→ **HalfOpen** (the first call after `BreakDuration` elapses is let through as a
single probe) → **Closed** again if the probe succeeds, or back to **Open** if it
fails.

Production values (`ResilienceTuningOptions.cs` defaults, used by both pipelines):

| Value | Redis | External service | Why |
|---|---|---|---|
| `FailureRatio` | 0.5 | 0.5 | Half of recent calls failing is a real, sustained problem, not one unlucky call. |
| `SamplingDuration` | 5s | 5s | The window the ratio is evaluated over. Short relative to day-5/task-6's 30s HTTP breaker deliberately — both of these are local, single-machine dependencies exercised by a demo button, not a production service under steady real traffic; a short window makes the breaker lifecycle observable in seconds rather than minutes without changing what it's actually proving. |
| `MinimumThroughput` | 4 | 4 | The ratio is never even evaluated until this many calls have happened in the window — so a single failed call can never trip the breaker on its own. Lower than day-5/task-6's 10 for the same "observable in seconds" reason. |
| `BreakDuration` | 10s | 10s | How long Open lasts before a probe is allowed. Polly.Core 8.7.0 enforces a hard minimum of >0.5s on both `BreakDuration` and `SamplingDuration` (see verification log) — 10s is comfortably above that floor and still short enough to watch happen. |
| Timeout | 2s | 2s | Well above real local latency (sub-millisecond for Redis on localhost; a few ms for the in-process HTTP call), well below the fault switch's 5s "Slow" delay, so a Slow-mode call reliably times out rather than racing to complete first. |
| Bulkhead permit / queue | 8 / 4 | 8 / 4 | Deliberately low so a demo button with a modest N can actually produce a rejection — not a realistic production concurrency ceiling. |

`OnOpened`, `OnHalfOpened`, and `OnClosed` are all wired to structured log lines (see
"Structured logging" below) — day-5/task-6 wired `OnOpened`/`OnClosed` but not
`OnHalfOpened`; this task adds it on both pipelines specifically because the exercise
asks for evidence of the half-open transition, not just open and closed.

## Structured logging

Every strategy event required by the brief is logged, with real structured state, not
just a formatted string: retry attempt (with attempt number, reason, and the real
`RetryDelay`), timeout fired (with the configured timeout), breaker opened (with
`BreakDuration`), breaker half-open, breaker closed, and bulkhead rejection. These
logs, captured for real (not hand-written), are the deliverable the exercise asks
for — see `output/` and `submission.md`.

## Graceful degradation when Redis is unavailable

This is the practical payoff of wrapping Redis at all, and it was verified working,
not just described: with the Redis circuit breaker Open, `GET
/api/authors/quote-summary/cached` still returns 200 with correct data, sourced from
the real database — HybridCache's own `GetOrCreateAsync` (Day 21, untouched) catches
the L2 failure (now surfaced as `Polly.CircuitBreaker.BrokenCircuitException` instead
of Day 21's `StackExchange.Redis.RedisConnectionException`, since Day 22 adds the
breaker in front of the same call path) and falls back to its factory, which reads
the database directly. Confirmed both by a dedicated xUnit test
(`RedisBreakerOpen_CachedEndpoint_StillServesCorrectDataFromTheDatabase`) and by
driving it live: opening the Redis breaker via the fault-injection switch, then
calling the real cached endpoint and confirming the DB-query counter shows 51 (the
real 1+N query shape) rather than a cache hit or a failure.

## Why the fault-injection switch exists, and that it is not production code

The exercise specifically requires showing the breaker open, go half-open, and
recover — which needs failure that can be turned on and off precisely and on demand.
Stopping the real local Redis container is too slow and imprecise to drive a
half-open transition on cue (Day 21 already used this technique for a different
purpose — proving Redis-down degradation — and it worked there because timing didn't
matter; here it does). `QuotesApi/Resilience/FaultInjectionSwitch.cs` is a
DI-registered, keyed-per-dependency mode toggle (`Healthy` / `Failing` / `Slow`),
checked from *inside* the real call path so the actual resilience pipeline reacts to
it exactly as it would to a real failure. Every file under `QuotesApi/Resilience/`
that exists purely for this purpose says so in its own header comment. None of it
ships with a real dependency's actual failure mode baked in — a real Redis outage or
a real slow external service triggers the same pipelines the same way, this switch
just makes it possible to trigger on command instead of waiting for one.

## How this goes beyond day-5/task-6/ResilienceDemo

Read in full before building this (see PROVENANCE.md for exactly what was read).
day-5/task-6 covers three strategies (retry, circuit breaker, timeout) against one
dependency, using `Microsoft.Extensions.Http.Resilience`'s standard HTTP-typed
options, with `OnOpened`/`OnClosed` logging and a `ScriptedHandler`-based test
harness — genuinely solid prior art, and this task's HTTP pipeline configuration and
test style deliberately follow its established patterns rather than reinventing them
(the retry shape, the "day-5/task-6 already established this reasoning" callouts
throughout this file, and the copied `ScriptedHandler.cs` /
`CapturingLoggerProvider.cs` test helpers are all direct continuations of it, with
attribution). Day 22 goes further on every axis the brief asks for: **four**
strategies including a bulkhead (day-5/task-6 has no concurrency limiter at all);
**two** dependencies with genuinely different pipelines, one of them (Redis) not
HTTP-shaped at all, requiring the lower-level non-generic `Polly.Core` API rather
than the HTTP-specific package; explicit `OnHalfOpened` logging (day-5/task-6 wires
open/closed only); a live, on-demand fault-injection mechanism instead of a
test-only fake handler, reachable from a running app and a browser demo page, not
just from `dotnet test`; and a real, working, verified graceful-degradation path for
Redis (day-5/task-6 has one dependency and no cache in front of it, so this concern
doesn't arise there).

## The HTTP dependency is in-process, and what a real one would look like

`GET /api/external/quote-of-the-day` lives inside this same `QuotesApi` process,
called back into via a normal named `HttpClient` whose `BaseAddress` resolves to this
app's own bound address. This is convenience, not architecture — a real external
dependency would be a genuinely separate process (its own deployment, its own
failure modes independent of this app's health, reachable over a real network hop),
and calling it would look identical from this app's side: the same
`ExternalServiceClient`, the same named `HttpClient`, the same resilience pipeline,
just pointed at a different `BaseAddress` via configuration. Nothing about the
resilience wiring assumes the dependency is local; only the fault-injection switch
(explicitly test/demo scaffolding, see above) does.

## Honest limits of this demonstration

- All timing values (5s sampling, 10s break, 2s timeout) are tuned for a demo you can
  watch happen in well under a minute, not calibrated against any real production
  traffic pattern or real dependency SLA.
- The "external service" has no real network hop, no real DNS, no real TLS handshake,
  no real connection-pool exhaustion under genuine load — it's exercising the
  resilience *code path*, not real network failure modes.
- The bulkhead permit/queue numbers (8/4) are chosen to be demonstrable with a
  two-digit N, not sized against any real capacity planning.
- The scripted evidence capture (`scripts/capture-resilience-evidence.sh`) hung once
  during a full run and had to be supplemented with a separate direct capture — see
  the verification log and `output/summary.md` for the complete, honest account.

## Verification log

1. **Real bug, caught live — `RedisCache` doesn't have the 2-argument constructor
   assumed from the XML docs.** Registering the resilience-wrapped `IDistributedCache`
   initially called `new RedisCache(IOptions<RedisCacheOptions>, ILogger)` — the XML
   docs for `Microsoft.Extensions.Caching.StackExchangeRedis` list that overload, but
   the actual build failed with `CS1729: 'RedisCache' does not contain a constructor
   that takes 2 arguments`. Fixed by using the plain 1-argument constructor
   (`IOptions<RedisCacheOptions>` only), confirmed by a clean build immediately after.
2. **Real bug, caught live — `CircuitBreakerStateProvider.IsInitialized` doesn't
   compile either**, despite appearing in the same XML docs alongside `CircuitState`.
   Removed the `IsInitialized` guard and instead force the Redis/HTTP pipelines to
   have been built at least once (by resolving `IDistributedCache` and
   `ExternalServiceClient`) before reading `/api/resilience/breakers`, so
   `CircuitState` is always safely readable.
3. **Real bug, caught live — a Redis-down 500 that Day 21's fix no longer covered.**
   Day 21 taught `AuthorQuoteSummaryCacheService.EvictAsync` to catch
   `StackExchange.Redis.RedisConnectionException` from a down Redis. Once Day 22 put
   a circuit breaker in front of Redis, opening that breaker (via the fault-injection
   switch) and calling `POST /api/measurement/reset` still 500'd — the exception
   surfacing from an *open breaker* is `Polly.CircuitBreaker.BrokenCircuitException`,
   a different type Day 21's catch clause never covered. Caught by actually opening
   the breaker and calling reset, not assumed. Fixed by catching both exception types.
4. **Real bug, caught live — the self-referencing HTTP client silently called the
   wrong port.** `ExternalServiceClient`'s named `HttpClient` defaulted its
   `BaseAddress` to a hardcoded `http://localhost:5000`. Running the app on a
   different port (5010, to dodge port 5000 already being held by macOS's AirPlay
   Receiver on this machine) made every "external" call come back a fast, unretried
   403 — the client was silently calling AirPlay's port, not this app's, and 403
   isn't in the retry strategy's default handled-status set so it never even looked
   like a resilience problem at first glance. Fixed by deriving the default from the
   same `"urls"` configuration key `ASPNETCORE_URLS`/`--urls` populates, read once
   before `builder.Build()`.
5. **Real bug, caught live — Polly.Core 8.7.0 enforces hard minimums the docs state
   but code doesn't defend against.** Early test tuning used a 200–300ms
   `BreakDuration`/`SamplingDuration` and `RetryMaxAttempts = 0` (to suppress retry
   noise in breaker-only tests) — both raised a real
   `System.ComponentModel.DataAnnotations.ValidationException` at pipeline-build time
   ("must be between 00:00:00.5000000 and 1.00:00:00", "MaxRetryAttempts must be
   between 1 and 2147483647"). Fixed by raising the shortest test `BreakDuration` to
   600ms and building a genuinely retry-free, breaker-only isolated pipeline for the
   breaker-lifecycle tests (mirroring day-5/task-6's own `CreateCircuitBreakerOnlyClient`
   pattern) instead of trying to configure retry down to zero.
6. **Real, not-a-bug finding — jitter can make a later retry delay shorter than an
   earlier one.** A test asserting each retry's `RetryDelay` was strictly greater than
   the previous one failed with a genuine captured pair (25.9ms then 30.8ms — the
   *next* attempt after 30.8ms was 25.9ms, not longer). This is expected behaviour of
   `UseJitter = true`, not a defect; the retry-delay-shape test now disables jitter to
   assert deterministic exponential growth, while jitter's real effect is left on
   (matching production) everywhere else and is independently visible in the real
   captured delays in `output/retry-backoff.txt` (e.g. one real sequence: 0.935s →
   0.577s → 2.788s).
7. **Real bug, caught live — `WebApplicationFactory.Server` accessed too early
   deadlocks the host.** `ResilienceApiFactory` originally passed the method group
   `Server.CreateHandler` directly to `ConfigurePrimaryHttpMessageHandler`. A method
   group still evaluates the `Server` property immediately, at the point the
   registration call executes — i.e. synchronously, from inside `ConfigureWebHost`,
   which runs *during* this same host's construction. Reading `Server` that early
   reenters host-building and hung every test using this factory indefinitely (caught
   live: `dotnet test` sat at ~98% CPU for minutes instead of failing or passing).
   Fixed by wrapping in a lambda (`() => Server.CreateHandler()`), which defers both
   the property read and the call until `IHttpClientFactory` actually needs the
   handler, well after the host has started.
8. **Real, documented limitation — the evidence-capture script hung once and was not
   made to reliably reproduce end-to-end.** See "Honest limits" above and
   `output/summary.md` for the full, honest account: a full run got through 4 of 5
   scenarios cleanly, then stopped producing output; the timeout and bulkhead
   evidence in `output/` was captured via a separate, direct, minimal run instead. The
   script now bounds every curl call with `--max-time 20` as a reasoned defensive fix
   for the most likely cause (a backgrounded curl process blocking indefinitely on a
   connection), but this was not re-verified with a full clean run because of the
   real time cost of doing so.

## How to run it locally

```bash
# 1. Start Redis (if not already running)
docker run -d --name day21-redis -p 6379:6379 redis:7.4-alpine
# (if the container already exists: docker start day21-redis)

# 2. Run the API (ASPNETCORE_ENVIRONMENT required, no launchSettings.json here - same
#    as Day 21/day-3/task-3. Port 5000 is unreliable on this machine specifically -
#    macOS's AirPlay Receiver also listens there and the bind sometimes fails with
#    "address already in use" and sometimes doesn't; 5299 avoids that ambiguity)
cd day-22/task-1/QuotesApi
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5299 dotnet run

# 3. Open the demo page
open http://localhost:5299/demo.html
```

To stop Redis afterward: `docker stop day21-redis` (add `docker rm day21-redis` to
remove the container entirely).

To reproduce the captured evidence in `output/`:

```bash
cd day-22/task-1
./scripts/capture-resilience-evidence.sh
```
