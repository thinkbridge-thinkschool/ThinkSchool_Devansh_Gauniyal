# Day 20, Task 1 — The Outbox Pattern

## The problem: dual writes diverge

A domain write (create the order) and a message publish (tell the rest of
the system about it) are two separate I/O operations against two separate
systems: the database and the broker. There is no distributed transaction
across them here, so any ordering of "write DB, then publish" or "publish,
then write DB" has a window where one succeeds and the other doesn't:

- Write DB, then crash before publish → the order exists but nobody downstream
  ever hears about it. Silent data loss from the consumer's point of view.
- Publish, then crash before the DB write → downstream systems react to an
  order that, as far as the source of truth is concerned, never happened.

Retrying the publish blindly doesn't fix this either, because a retry after
a partial failure is itself indistinguishable from a duplicate.

## The fix: transactional outbox

Instead of writing to the DB and publishing to the broker as two operations,
write to the DB **twice**, in one transaction: once for the domain change,
once for an `OutboxMessage` row describing what should be published. Both
rows commit together or neither does — ordinary ACID guarantees from a
single `SaveChangesAsync()` call, no distributed transaction required.

A separate **relay** then polls the outbox table, publishes each unprocessed
row, and marks it sent only after the publish call returns successfully.
The relay can crash, retry, or run late, and none of that touches whether
the original domain write and its outbox row are still there together.

This trades "exactly-once, always consistent" (unachievable across two
systems without a distributed transaction) for **at-least-once delivery**
with **atomic bookkeeping on the DB side** — which is the honest, achievable
guarantee, described below.

## The two crash windows

The relay's per-message work is: `publish()` then `mark sent()`. A crash can
land on either side of that boundary.

**Crash A — dies after the DB commit, before the publish is even attempted.**
The outbox row is sitting there, `ProcessedOn = NULL`, `AttemptCount = 0`.
Nothing was ever sent. When the relay (or a new instance of it) starts again,
it finds this row exactly as any other unprocessed row and publishes it.
**Cost: at most a delay.** Nothing is lost.

**Crash B — dies after a successful publish, before `ProcessedOn` is
written.** The message has genuinely gone out — a downstream system may
already be acting on it — but the outbox row still looks unprocessed. On
restart, the relay finds it again (or that instance's claim lease has since
expired for another instance to find it) and publishes it a **second time**.
**Cost: a duplicate delivery, not a loss.**

Both crash windows are proven in this repo with a **real, injected failure**,
not a code comment — see [Crash tests](#crash-tests) below.

## At-least-once, not exactly-once — and why

The honest claim this implementation makes is:

> No message is ever lost. A message may be delivered more than once.
> Duplicates are made harmless by an idempotent consumer.

It does **not** claim exactly-once end-to-end delivery, because that would
require the publish step and the "mark sent" step to be a single atomic
operation across two different systems (the DB and the broker), which is
exactly the dual-write problem this pattern exists to avoid. At-least-once
plus an idempotent consumer is the standard, achievable substitute — and
it's a substitute, not a workaround: real brokers (Service Bus, SQS, Kafka)
all give the same at-least-once guarantee for the same reason, so this
constraint isn't specific to the fake publisher used here.

## The idempotent consumer

The consumer in [`IdempotentConsumer.cs`](src/OutboxDemo/Consumer/IdempotentConsumer.cs)
dedupes on the outbox message id via a `ProcessedMessages` table whose
primary key **is** that id. A second delivery of the same message either:

- finds the row already there (a plain `SELECT` check), or
- loses a race to insert it (a `DbUpdateException` on the primary key,
  caught and treated as a duplicate too),

and either way the "real" side effect (recorded here as an in-memory list
standing in for whatever the consumer actually does — charge a card, send an
email, decrement stock) only ever runs once. Using a **durable** table for
this, rather than an in-memory set, matters: the consumer's own process can
restart between two deliveries of the same message, and an in-memory dedupe
set would forget everything a crash-and-restart of the consumer itself
would need to remember.

## Relay concurrency: the claiming strategy

Two relay instances (two API replicas, a horizontally scaled worker, a
manual run overlapping the background poll) must not both publish the same
row. The strategy used here is a **claim column with owner and lease**:

```csharp
var claimedRows = await _db.OutboxMessages
    .Where(m => m.Id == id && m.ProcessedOn == null
             && (m.ClaimedBy == null || m.ClaimedUntil < claimNow))
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(m => m.ClaimedBy, _ownerId)
        .SetProperty(m => m.ClaimedUntil, claimNow.Add(_leaseDuration)));

if (claimedRows == 0) { /* someone else has it */ }
```

`ExecuteUpdateAsync` compiles to a single `UPDATE ... WHERE ...` statement.
The database, not application code, decides atomically whether the `WHERE`
still matched by the time the write actually happens — there is no
read-then-write race in application code to get wrong, because there is no
read step at all in the claim itself. If the row was already claimed (and
the lease hasn't expired), zero rows are affected and this instance moves
on. If the claiming instance then dies mid-publish, the lease simply expires
and another instance is free to reclaim the row — which is precisely Crash B.

**What SQLite can and cannot prove here, honestly:** SQLite's *entire
database file* is single-writer — every write transaction (including a bare
`UPDATE`) is serialized by SQLite itself, with a second concurrent writer
either blocking (given a `PRAGMA busy_timeout`, which this project sets) or
failing with `SQLITE_BUSY`. That serialization is exactly what makes the
`ExecuteUpdateAsync` compare-and-swap race-free in the concurrency test
below — but it proves the claim logic is *correct*, not that it *scales*.
SQL Server (or Postgres) would let genuinely concurrent writers proceed in
parallel against different rows via row-level locking, and would be the
right choice to also prove throughput under real multi-process contention.
This project doesn't need that: the crash proofs are driven by deterministic
injected failures, not by real timing races, so SQLite's single-writer
guarantee is sufficient to prove the claiming logic is correct, and honestly
insufficient to say anything about how it behaves under load.

## Database choice: SQLite

Chosen over SQL Server via Testcontainers for this task specifically
because:

- The crash proofs are driven by **deterministic injected failures** (a
  `SimulatedCrashException` thrown at a precise point, a `TimeProvider` the
  tests can fast-forward) rather than by real process kills or real timing
  races — so SQL Server's richer row-locking semantics wouldn't add proof
  value here that SQLite's single-writer serialization doesn't already give
  for the *correctness* of the claim.
- No Docker dependency for `dotnet test` to pass, which matters since a
  mentor should be able to clone and run this without also standing up a
  container.
- The honest cost, stated above, is that this doesn't prove behavior under
  real multi-process row-level contention. A production version of this
  relay against SQL Server or Postgres, under real concurrent load, is where
  that would need to be re-verified.

Each test gets its own throwaway SQLite file under the test project's own
`bin/` output (never outside `day-20/task-1`), migrated fresh and deleted on
disposal — see [`SqliteTestDatabase.cs`](tests/OutboxDemo.Tests/TestSupport/SqliteTestDatabase.cs).

## Polling relay vs. change-data-capture

This relay polls the outbox table on an interval (`OutboxRelayBackgroundService`,
a `BackgroundService` running every 2 seconds) and is also callable directly
via `POST /relay/run` for on-demand or test-driven runs. The alternative is
CDC — tailing the database's write-ahead/transaction log (e.g. Debezium
against SQL Server CDC or Postgres logical replication) to react to new
outbox rows the moment they commit, with no polling delay and no wasted
queries when the table is empty.

Polling was chosen here because it needs nothing beyond EF Core and the
target database — no CDC agent, no replication slot, no extra
infrastructure — which matches the "no extra packages, no Azure, no spend"
constraint on this task. The honest tradeoff: polling adds up to one poll
interval of latency and issues a "any work to do?" query even when the
answer is no, while CDC has near-zero latency and no wasted queries but
needs an extra moving part to run and operate.

## Crash tests

Both crash windows are exercised in
[`CrashTests.cs`](tests/OutboxDemo.Tests/CrashTests.cs) with a *real* forced
failure at a precise point — not a comment asserting what would happen:

- **`CrashA_DiesAfterCommitBeforePublish_MessageIsNotLost`** commits the
  order + outbox row, then simply never calls the relay in that "process
  lifetime" — the equivalent of a crash landing before publish is even
  attempted. A fresh `DbContext`, publisher and relay (the "restart") then
  picks the row up and publishes it. Evidence:
  [`output/crash-a-evidence.json`](output/crash-a-evidence.json).

- **`CrashB_DiesAfterPublishBeforeMarkSent_MessageIsDuplicatedButConsumerDedupes`**
  sets `OutboxRelayService.CrashAfterPublishBeforeMarkSent` to fire right
  after a successful publish, and asserts the resulting
  `SimulatedCrashException` actually propagates out of `ProcessOnceAsync`
  uncaught — exactly like a real process death would, not a caught-and-
  retried failure. A `ManualTimeProvider` is then advanced past the dead
  instance's claim lease (no `Thread.Sleep`, no wall-clock wait) and a
  second relay instance republishes the same row. The test asserts the
  publisher/consumer wiring saw the message **twice** but the durable
  dedupe table only ever recorded **one** processed row. Evidence:
  [`output/crash-b-evidence.json`](output/crash-b-evidence.json).

Both evidence files are regenerated every time the test suite runs
(`dotnet test`), by the tests themselves.

## Publishing: an in-process fake, not a real broker

The brief asks for "queue publish" generically, without naming a broker.
[`InProcessFakePublisher`](src/OutboxDemo/Publishing/InProcessFakePublisher.cs)
sits behind `IMessagePublisher` and hands messages straight to the
in-process consumer, with a `FailureInjector` hook for deterministically
forcing publish failures or crashes in tests. This keeps the outbox
mechanics — atomicity, claiming, retry, crash recovery, dedup — fully and
deterministically testable without provisioning or paying for a real broker.
Swapping in a real one later (Azure Service Bus, SQS, Kafka) means writing a
new `IMessagePublisher` implementation; nothing else in this project changes.

## EF Core relationships

Treated as a topic label on the exercise, not a separate deliverable: no
extra entity or feature was built solely to exercise it. `Order` and
`OutboxMessage` have a real one-to-many relationship (`Order.OutboxMessages`,
`OutboxMessage.OrderId` as the FK, configured in
[`AppDbContext.OnModelCreating`](src/OutboxDemo/Data/AppDbContext.cs)),
which the outbox mechanics exercise naturally as part of doing their actual
job.

## Verification log

- **`dotnet build`** — clean, 0 errors. The only warnings are `NU1903`
  supply-chain advisories on `Microsoft.OpenApi` (pulled in transitively by
  the default ASP.NET Core minimal-API template's `Microsoft.AspNetCore.OpenApi`
  package) and `SQLitePCLRaw.lib.e_sqlite3` (pulled in transitively by
  `Microsoft.EntityFrameworkCore.Sqlite`) — both are the current published
  versions of those packages at the pinned EF Core 10 / template version
  used here, not something introduced by this project's own code.
- **`dotnet test`** — 10/10 passing (see [submission.md](submission.md) for
  the full list and the mutation-check transcript).
- **Real bug caught while building this:** the first version of the
  concurrency test (`TwoConcurrentRelays_DoNotDoublePublish_TheSameRow`)
  asserted that the *losing* relay instance would always show up in
  `SkippedClaimedByOther` — i.e., that it would see the row as a candidate
  and then lose the claim race. Running it failed with `Expected: 1,
  Actual: 0`. The actual (correct) implementation behavior is looser than
  that: depending on exact timing, the losing instance can just as validly
  see **zero** candidates at all, because the winning instance's claim had
  already committed before the loser's own `SELECT` ran. Both outcomes are
  safe — no double publish either way — so the bug was in the test's
  assertion, not the relay. Fixed by asserting only the property that
  actually matters: `publisher.CallCount == 1` and total `Published.Count
  == 1` across both instances. No change to
  [`OutboxRelayService.cs`](src/OutboxDemo/Relay/OutboxRelayService.cs) was
  needed.
- **Required mutation check** (see submission.md for the real terminal
  output): moved the `ProcessedOn` write to *before* the publish call
  instead of after. Three tests failed for the right reason — rows were
  marked sent even though the publish had not (yet, or ever, on the
  failure-path test) succeeded. Reverted; suite back to 10/10 green.
