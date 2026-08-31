namespace BackgroundJobsDemo.Jobs;

public interface IJobStatusStore
{
    JobRecord Create();

    void MarkRunning(Guid id);

    void MarkCompleted(Guid id);

    void MarkFailed(Guid id, string error);

    bool TryGet(Guid id, out JobRecord record);

    IReadOnlyCollection<JobRecord> GetAll();
}
