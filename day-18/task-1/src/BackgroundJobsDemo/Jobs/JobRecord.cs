namespace BackgroundJobsDemo.Jobs;

public sealed record JobRecord(Guid Id, JobStatus Status, string? Error = null);
