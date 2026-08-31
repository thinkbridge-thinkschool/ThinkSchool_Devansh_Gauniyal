using BackgroundJobsDemo.Queue;

namespace BackgroundJobsDemo.Jobs;

public static class JobsEndpoints
{
    // Simulated slow work. The real duration doesn't matter to the proof -- POST returns before
    // this delay ever starts, because the delay runs inside the queued work item, on the
    // background worker, not on the request thread.
    private static readonly TimeSpan SimulatedWorkDuration = TimeSpan.FromMilliseconds(300);

    public static void MapJobsEndpoints(this WebApplication app)
    {
        app.MapPost("/api/jobs", async (IBackgroundTaskQueue queue, IJobStatusStore store) =>
        {
            var job = store.Create();

            await queue.EnqueueAsync(async cancellationToken =>
            {
                store.MarkRunning(job.Id);
                try
                {
                    await Task.Delay(SimulatedWorkDuration, cancellationToken);
                    store.MarkCompleted(job.Id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    store.MarkFailed(job.Id, ex.Message);
                    throw;
                }
            });

            return Results.Accepted($"/api/jobs/{job.Id}", new { id = job.Id });
        });

        app.MapGet("/api/jobs/{id:guid}", (Guid id, IJobStatusStore store) =>
            store.TryGet(id, out var record) ? Results.Ok(record) : Results.NotFound());

        app.MapGet("/api/jobs", (IJobStatusStore store) => Results.Ok(store.GetAll()));
    }
}
