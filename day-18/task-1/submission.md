# Day 18 Task 1 — Background jobs

## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-18/task-1/day-18/task-1

(Branch `day-18/task-1`.)

## Notes for mentor

**The BackgroundService, in full:**

```csharp
using BackgroundJobsDemo.Queue;

namespace BackgroundJobsDemo.Worker;

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
                // anything still sitting in the queue is deliberately abandoned -- see README,
                // "Graceful shutdown: what happens to queued work".
                break;
            }

            try
            {
                await workItem(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown arrived while this item was running. Also a normal stop: this demo
                // abandons the in-flight item rather than guaranteeing it finishes -- see README.
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

**How it shuts down cleanly:** `StopAsync` logs, then calls `base.StopAsync`, which cancels
`stoppingToken` and awaits `ExecuteAsync`'s task, bounded by `HostOptions.ShutdownTimeout` (set to
10 seconds in `Program.cs`). Inside the loop, a cancelled dequeue-wait or a cancelled in-flight
item is caught explicitly and treated as a normal exit (`break`), never logged as an error. This
demo deliberately abandons anything still queued or in-flight rather than draining it — full
reasoning and a real test proving it in `README.md`.

**One line — when Hangfire over a hosted service?** Reach for Hangfire the moment a job needs to
survive an app restart, retry automatically, run on a cron schedule, or be coordinated safely
across more than one running instance of the app.

**Scope resolution:** Hangfire is treated as contrast only, per this task's own wording — no
package installed, no server run, no storage added; covered in prose in `README.md` and the one
line above.

## What did you learn this session?

BackgroundService already does the hard part of a clean shutdown — I just had to pass the cancellation token all the way through and decide upfront what happens to work still waiting. The real design choice here wasn't the code itself, it was honestly picking between finishing queued work or abandoning it.

## What would break this?

If someone swapped the small bounded queue for a huge one to "stop it blocking requests," the backpressure signal disappears and the single worker could silently fall arbitrarily far behind. And since nothing here is saved to disk, any job still queued or mid-run when the process stops is just gone, with no record it ever existed.
