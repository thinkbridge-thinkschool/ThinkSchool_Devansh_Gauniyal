namespace ChangeTrackerDemo;

// Locates day-10/task-1 on disk by walking up from wherever the assembly is
// actually running, so the same logic works whether invoked via `dotnet run`
// from this folder or from within the test project's bin output.
public static class TaskPaths
{
    public static string FindTaskRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Task1.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not locate day-10/task-1 root (no Task1.slnx found in any parent directory).");
        }

        return dir.FullName;
    }

    public static string OutputDirectory() => Path.Combine(FindTaskRoot(), "output");

    public static string ResultsFilePath() => Path.Combine(OutputDirectory(), "results.json");

    public static string SubmissionFilePath() => Path.Combine(FindTaskRoot(), "submission.md");

    public static string TrackingBenchmarkSourcePath() =>
        Path.Combine(FindTaskRoot(), "ChangeTrackerDemo", "TrackingBenchmark.cs");
}
