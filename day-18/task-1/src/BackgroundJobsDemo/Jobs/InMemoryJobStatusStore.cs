using System.Collections.Concurrent;

namespace BackgroundJobsDemo.Jobs;

public sealed class InMemoryJobStatusStore : IJobStatusStore
{
    private readonly ConcurrentDictionary<Guid, JobRecord> _jobs = new();

    public JobRecord Create()
    {
        var record = new JobRecord(Guid.NewGuid(), JobStatus.Queued);
        _jobs[record.Id] = record;
        return record;
    }

    public void MarkRunning(Guid id) => Update(id, record => record with { Status = JobStatus.Running });

    public void MarkCompleted(Guid id) => Update(id, record => record with { Status = JobStatus.Completed });

    public void MarkFailed(Guid id, string error) => Update(id, record => record with { Status = JobStatus.Failed, Error = error });

    public bool TryGet(Guid id, out JobRecord record) => _jobs.TryGetValue(id, out record!);

    public IReadOnlyCollection<JobRecord> GetAll() => _jobs.Values.ToArray();

    private void Update(Guid id, Func<JobRecord, JobRecord> update)
    {
        _jobs.AddOrUpdate(
            id,
            _ => throw new InvalidOperationException($"Job '{id}' was never created."),
            (_, existing) => update(existing));
    }
}
