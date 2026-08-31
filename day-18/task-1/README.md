# Day 18 Task 1 — Background jobs

A standalone .NET 10 solution demonstrating moving slow work off the request thread with a
bounded `System.Threading.Channels` queue and a `BackgroundService` worker, with a deliberate,
documented graceful-shutdown behaviour.

This is a new, standalone solution. It does not reuse, copy, or reference any prior day's code
(`day-3/task-3/QuotesApi` included) — there is no continuous .NET project chain elsewhere in this
repository to build on, unlike the Angular app.

## Structure

```
day-18/task-1/
  BackgroundJobs.slnx
  src/BackgroundJobsDemo/          ASP.NET Core Minimal API (no controllers)
    Queue/IBackgroundTaskQueue.cs
    Queue/ChannelBackgroundTaskQueue.cs
    Worker/QueuedHostedService.cs
    Jobs/JobStatus.cs, JobRecord.cs, IJobStatusStore.cs, InMemoryJobStatusStore.cs, JobsEndpoints.cs
    Program.cs
  tests/BackgroundJobsDemo.Tests/  xUnit
```

Named `BackgroundJobs.slnx` rather than the repo's more common `TaskN.slnx` pattern (e.g.
`Task1.slnx`) — a descriptive name reads better for a topic this specific, and it's what this
task's own instructions specified explicitly. Everything else follows repo convention: `net10.0`
throughout, a per-task `.gitignore` (see below), `src`/`tests` split.

## Scope resolution (recorded so a mentor can challenge it)

- **"WHAT THIS BUILDS: Background jobs"** is read as a topic label, not an extra deliverable —
  nothing was built solely to satisfy that line.
- **Hangfire is contrast only.** No Hangfire package is installed, no Hangfire server runs, no
  SQL/Redis storage was added. It's covered in prose below and in one line in `submission.md`.

## The queue: a bounded channel, on purpose

`ChannelBackgroundTaskQueue` wraps `Channel.CreateBounded<Func<CancellationToken, Task>>` with
`FullMode = BoundedChannelFullMode.Wait` and a capacity of **3**.

**Why bounded, and why 3:** an unbounded queue in front of a single worker means a burst of
producers can queue unlimited in-memory work with no signal that the worker is falling behind —
the failure mode is silent memory growth instead of a decision. A bounded channel forces that
decision up front. Capacity 3 is deliberately small: this demo holds transient, non-persisted
work (there's no database or disk backing it — a crash loses whatever's still queued regardless
of capacity), so a large buffer would only hide backpressure instead of making it observable. A
small number makes the backpressure test deterministic and fast (fill 2 slots, prove a 3rd
`EnqueueAsync` call is genuinely still pending, free a slot, prove it then completes) rather than
needing thousands of items and a timing guess. A real system would size this from measured
producer/consumer throughput, not this default.

**Why `FullMode.Wait`, and the trade-off it implies:** when the channel is full, `EnqueueAsync`
awaits free capacity rather than throwing or silently dropping the new item. Concretely, that
means `POST /api/jobs` itself will not return 202 until a slot frees up if three jobs are already
queued — the caller feels the backpressure directly. The alternative (fail fast with a 503/429
when full) would keep the API always-responsive at the cost of turning a burst into lost work the
caller has to notice and retry. This demo chose to make the queue's own limit the API's limit,
since that's the simplest correct behaviour and the one that actually demonstrates a bounded
channel doing its job; a production API fronting a queue like this might choose the fail-fast
alternative instead, once losing a burst is an acceptable (and monitored) outcome.

Null work items are rejected synchronously (`ArgumentNullException` from `EnqueueAsync`) rather
than silently queued and only failing later when dequeued.

## The worker: `QueuedHostedService`

```csharp
public sealed class QueuedHostedService(
    IBackgroundTaskQueue taskQueue,
    ILogger<QueuedHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("QueuedHostedService starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            Func<CancellationToken, Task> workItem;
            try
            {
                workItem = await taskQueue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown arrived while waiting for the next item. Normal stop, not an error;
                // anything still sitting in the queue is deliberately abandoned -- see
                // "Graceful shutdown: what happens to queued work" below.
                break;
            }

            try
            {
                await workItem(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown arrived while this item was running. Also a normal stop: this demo
                // abandons the in-flight item rather than guaranteeing it finishes.
                break;
            }
            catch (Exception ex)
            {
                // A single bad job must never take the drain loop down with it.
                logger.LogError(ex, "Background work item threw and was skipped.");
            }
        }

        logger.LogInformation("QueuedHostedService stopping.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("QueuedHostedService stop requested.");
        await base.StopAsync(cancellationToken);
        logger.LogInformation("QueuedHostedService stopped.");
    }
}
```

Three things worth calling out:

- **The two `catch` blocks around `DequeueAsync` and `workItem(...)` are deliberately separate.**
  A cancelled *wait for the next item* and a cancelled *item currently running* are different
  moments in the shutdown story, and collapsing them into one `try` would blur which one actually
  happened when reading a stack trace or a log.
- **`OperationCanceledException` is filtered by `stoppingToken.IsCancellationRequested`**, not
  caught unconditionally. An item that cancels itself for its own reasons (a timeout it set
  internally, for instance) is a different situation from the host shutting down, and only the
  latter should be treated as "normal, stop the loop."
- **The generic `catch (Exception ex)` only wraps the item's execution, not the dequeue.** A
  queue implementation failing is a bug in the plumbing, not "one bad job" — it shouldn't be
  swallowed the same way.

## Graceful shutdown: what happens to queued work

Two honest options existed here: drain everything still queued (and the in-flight item) within
the shutdown timeout, or abandon anything not already finished. **This demo abandons.**

Concretely, at shutdown:
- If the worker is parked in `DequeueAsync` waiting for the next item, that read is cancelled
  immediately — no further items are ever dequeued.
- If an item is already running, `stoppingToken` is passed straight into it. This demo's own
  simulated job (`Task.Delay(..., cancellationToken)`) honours that token, so an in-flight job is
  itself interrupted, not given a grace period to finish.
- Anything still sitting in the bounded channel — enqueued, never started — is never executed.
  It is not persisted anywhere, so it is simply gone once the process stops.

This is proven, not just claimed: `QueuedHostedServiceTests.Shutdown_AbandonsTheInFlightItemAndNeverStartsStillQueuedItems`
enqueues two items, lets the first start, stops the host, and asserts the first item genuinely
observed `OperationCanceledException` (interrupted, not left to finish) while the second item's
own start signal never fired (never dequeued at all).

**The trade-off, stated plainly:** this is fast and predictable — shutdown doesn't hang waiting
for a queue to drain — but it means zero completion guarantee for whatever hadn't already
finished. That's an acceptable choice for a demo with an in-memory, non-persisted queue: nothing
here survives a crash either, so pretending a graceful `SIGTERM` behaves differently from a crash
would be misleading. A system that actually needs a "finish what's already accepted" guarantee
would need either a real drain loop (bounded by the shutdown timeout, continuing to dequeue and
run items until the channel is empty or time runs out) or, more robustly, a durable queue that
survives the process entirely — which is exactly the gap Hangfire (see below) is built to close.

`HostOptions.ShutdownTimeout` is set explicitly to **10 seconds** (`Program.cs`), up from the
framework default of 5. That number is a safety margin, not a drain budget: the ASP.NET Core web
host is also draining in-flight HTTP requests in the same window, and 10 seconds gives both that
and this worker's cancellation-observation (logging, unwinding a `try/finally`, if the in-flight
item has one) enough headroom to finish cleanly without the process hanging indefinitely if
something takes a moment longer than expected.

## The API: proving "off the request thread"

- `POST /api/jobs` — creates a job record (`Queued`), enqueues a work item that simulates 300ms of
  work, and returns `202 Accepted` with `{ id }` and a `Location` header — **before** that 300ms
  delay ever starts, because the delay runs inside the queued delegate on the worker, not inline
  in the request handler.
- `GET /api/jobs/{id}` — returns the job's current status (`Queued` → `Running` →
  `Completed`/`Failed`), or `404` if the id is unknown.
- `GET /api/jobs` — lists every job's current status.

No auth, no database, no persistence (an in-memory `ConcurrentDictionary`-backed store), no
Docker, no extra runtime packages — the API project itself references nothing beyond the ASP.NET
Core shared framework. (The *test* project references `Microsoft.AspNetCore.Mvc.Testing` for
`WebApplicationFactory`-based integration tests — a standard testing-only dependency, not part of
the shipped API's footprint.)

`JobStatus` is serialized as a string (`Queued`/`Running`/...), not its underlying integer —
`Program.cs` registers a `JsonStringEnumConverter` for this, since the default numeric encoding
would make `GET` responses unreadable without cross-referencing the enum's source.

## BackgroundService vs raw IHostedService

`IHostedService` is the base contract: `StartAsync`/`StopAsync`, called once each, and you're on
your own for how (or whether) you run something continuously in between — including how you loop,
how you observe cancellation, and how you avoid blocking `StartAsync` itself (a naive
`Task.Run`-free implementation that awaits its own work loop directly inside `StartAsync` will
never return, which blocks the entire host's startup sequence behind it).

`BackgroundService` is Microsoft's own `IHostedService` implementation that removes exactly that
foot-gun: it implements `StartAsync` to kick off your `ExecuteAsync` on a background task without
awaiting it there, and implements `StopAsync` to signal cancellation and *then* await that task
(bounded by `HostOptions.ShutdownTimeout`). All the boilerplate this demo would otherwise need to
hand-write — not blocking startup, cancelling on shutdown, awaiting the running task with a
timeout — comes for free.

**When you'd still drop to raw `IHostedService`:** when "one long-running background loop" isn't
the shape of what you need. A service that only needs to *start* something once and tear it down
later (opening a long-lived connection, registering with an external system on boot) doesn't need
a loop at all, and forcing it through `BackgroundService`'s `ExecuteAsync` shape adds nothing. Or
a service that genuinely needs custom control over its own startup/shutdown sequencing — for
example, needing `StartAsync` to block until some readiness condition is met before the host
considers startup complete — needs the raw contract, because `BackgroundService.StartAsync` is
sealed against doing that (it fires and forgets `ExecuteAsync` by design).

## Hangfire vs an in-process hosted service

An in-process `BackgroundService` (this demo) and Hangfire solve overlapping but genuinely
different problems.

**What this demo has:** zero extra infrastructure (no storage dependency, nothing to deploy or
operate beyond the app itself), and it's the simplest possible answer to "run this off the
request thread, right now, in this process." What it does *not* have: anything survives a
restart. Retries, if wanted, would have to be hand-written into each work item. It cannot
coordinate across multiple instances of the app running behind a load balancer — each instance
has its own independent, in-memory queue, so scaling out multiplies the queues, it doesn't share
one. There's no dashboard; the only visibility is whatever gets logged.

**What Hangfire trades in for that:** persistence (jobs survive a restart or a crash, because
they live in SQL Server/Redis/etc., not in process memory), built-in retry policies, real cron
scheduling for recurring jobs, a dashboard for operational visibility, and multi-instance
coordination (several app instances can safely share one job store without double-processing the
same job). The cost is real: a storage dependency to provision, secure, and keep available, plus
an entirely separate operational surface (the Hangfire server component, its dashboard, its own
failure modes) to run and monitor — infrastructure this demo's `BackgroundService` has none of.

**One line: when Hangfire over a hosted service?** Reach for Hangfire the moment a job needs to
survive an app restart, retry automatically, run on a cron schedule, or be coordinated safely
across more than one running instance — a plain `BackgroundService` gives you none of those for
free.

## Local verification

```bash
cd day-18/task-1
dotnet build BackgroundJobs.slnx
dotnet test BackgroundJobs.slnx
dotnet run --project src/BackgroundJobsDemo
# in another shell:
curl -i -X POST http://localhost:<port>/api/jobs
curl -i http://localhost:<port>/api/jobs/<id-from-above>
```

## Verification log

- **Fresh build:** `dotnet build BackgroundJobs.slnx` — succeeded, 0 warnings, 0 errors, on the
  first attempt.
- **Fresh test run:** `dotnet test BackgroundJobs.slnx` — **11 passed, 0 failed, 0 skipped**, on
  the first attempt. No implementation bug was hit and fixed while building this — the design was
  worked out before writing code (in particular, the abandon-vs-drain shutdown decision above),
  and the first complete implementation built and passed cleanly.
- **One thing checked proactively, before it could become a bug:** `System.Text.Json` serializes
  a C# `enum` as its underlying integer by default. The API integration tests compare
  `JobRecord.Status` against string values (`"Completed"`, etc.), which would have failed against
  the default encoding. `JsonStringEnumConverter` was registered in `Program.cs` from the start,
  rather than discovered after a failing test — this is a known .NET gotcha checked up front, not
  a bug that was caught and fixed.
- **Required mutation check, run for real:** the per-item `try { await workItem(...) } catch
  (Exception ex) { ... }` block in `QueuedHostedService.ExecuteAsync` was temporarily deleted,
  leaving `await workItem(stoppingToken);` unguarded. Re-running the suite produced a real
  failure: `QueuedHostedServiceTests.ExecuteAsync_ThrowingWorkItem_IsLoggedAndDoesNotStopTheLoop`
  failed with `System.TimeoutException: The operation has timed out.` (10 passed, 1 failed) —
  because the first item's exception, now unhandled, terminated `ExecuteAsync` entirely, so the
  second item was never dequeued and its completion signal never fired within the test's 2-second
  bound. The catch block was then restored exactly as written above, and the suite was re-run:
  **11 passed, 0 failed** again. Full command output for both runs is in `submission.md`'s
  reviewer-facing summary and in this session's transcript.
- **Fresh repo-wide baseline, measured before any of this was added:** all 40 pre-existing
  solutions in the repository were run with `dotnet test` — **1014 passed, 0 failed, 0 skipped**
  across every one of them. `day-3/task-7` (Testcontainers-based SQL Server integration tests)
  passed all 15 for real against a live Docker container, since Docker happened to be running at
  the time — this repository's accepted baseline otherwise expects those 15 to fail with
  `DockerUnavailableException` when Docker is down. Unrelated to this task's own code, noted here
  only because Phase 1 of this task required measuring and reporting it.
- **Nothing in this task touches Docker, a database, or any prior day's code.** It has no
  dependency on the Docker state above one way or another.
