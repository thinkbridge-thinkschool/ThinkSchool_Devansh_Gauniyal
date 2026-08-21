# Day 11 Task 1 — Profile a slow endpoint

A deliberately slow endpoint, `GET /api/authors/quote-summary`, added directly to the
real Week-1 API (`day-3/task-3/QuotesApi`), reproducing two real data-access anti-patterns
on purpose so they can be measured, not fixed. The new code lives entirely under
`day-3/task-3/QuotesApi/Performance/` - additive only, nothing pre-existing in that project
was changed in behavior. This folder (`day-11/task-1`) holds the tests, the profiling
script, and the evidence captured from a real run.

## What N+1 is, and why it's easy to write by accident

An N+1 query pattern is one query to load a list of parent rows, followed by one more
query *per row* to load each parent's related data - N+1 round trips instead of one. It
is easy to write without noticing because the code that causes it looks completely
ordinary: `foreach (var author in authors) { /* touch author.Quotes here */ }`. Nothing
about that loop looks wrong on a small list in local dev. The problem only shows up as the
list grows, and by then the loop is buried a few calls deep and doesn't look like a
database access point at all. `AuthorQuoteSummaryQuery.Run` reproduces this exact shape:
it loads every author with one query, then inside the loop calls
`context.Entry(author).Collection(a => a.Quotes).Load()` for each one - an explicit,
realistic stand-in for the lazy-loading access that triggers this in production code.

## Why a missing FK index compounds it

Each of those N extra queries filters `Quotes` by `AuthorId`. Without an index on that
column, SQLite has no way to jump straight to the matching rows - it has to scan the whole
table and check every row. So the cost of the N+1 pattern isn't "N cheap lookups", it's "N
full table scans". Double the row count and every one of those N queries gets twice as
expensive, on top of N itself growing with the author count. The two problems multiply
each other rather than just adding.

## What p50 and p99 mean, and why p99 matters more

p50 (the median) is the latency half of requests beat and half didn't - it describes a
typical request. p99 is the latency 99% of requests beat - it describes the request a user
is unlucky enough to land on 1 time in 100. An average, or even the p50, can look perfectly
fine while a meaningful fraction of real users sit on a badly-behaved tail. A p99 far above
the p50 is exactly the signature an N+1-plus-missing-index bug leaves: most requests hit
warm state and OS/SQLite page caching and look fine, while some don't and pay the full
scan cost N times over.

## Why the absolute numbers are laptop-bound

This was measured on a single Apple Silicon (arm64) laptop, with the API and the load
generator (bombardier) running side by side, competing for the same CPU cores. That is not
how a real client and a real server relate to each other - a production client would be on
a different machine entirely, and the server wouldn't be sharing its CPU with the thing
measuring it. So the millisecond values in `output/load-test.txt` should not be read as
"how slow is this in production" - they should be read as "how much worse does the tail
get than the middle", which is a shape, not an absolute number.

## Why the fix is deliberately not included

This task's scope is measurement and diagnosis only: capture the baseline p50/p99, the
offending SQL, and the query plan, and name the two problems the evidence points to. It
does not ask for `Include()`, a projection, or an index migration - adding any of those
here would overwrite the baseline this task exists to produce. See `submission.md` for the
full reasoning, including why this modifies the real Week-1 API directly rather than a
separate stand-in.

## How to re-run everything

Requires the .NET 10 SDK, `openssl` (for generating throwaway bootstrap secrets - macOS
ships this by default), and either `bombardier` or `k6` on `PATH`.

```bash
cd day-11/task-1
scripts/run-profile.sh
```

This builds `day-3/task-3/QuotesApi` and this task's test project, starts the real
QuotesApi on a free local port (with freshly generated, throwaway synthetic bootstrap
secrets passed only as environment variables - never written to disk, never touching any
real configured secret), runs a short warmup pass (discarded), runs the real load test,
and captures the single-request SQL log, the query plan, and the schema dump - all into
`output/`. It always stops the API afterwards, even if a step fails.

To run just the tests (no load test, no running API required):

```bash
cd day-11/task-1
dotnet test Task1.slnx
```

The `ArtefactTests` in `QuotesApi.Performance.Tests` read the files under `output/` and
will fail until `scripts/run-profile.sh` has been run at least once - that is expected,
not a bug.
