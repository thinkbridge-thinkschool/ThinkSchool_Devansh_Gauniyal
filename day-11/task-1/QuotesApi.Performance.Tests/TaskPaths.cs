namespace QuotesApi.Performance.Tests;

// Locates day-11/task-1 on disk by walking up from wherever this test assembly is
// actually running.
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
                "Could not locate day-11/task-1 root (no Task1.slnx found in any parent directory).");
        }

        return dir.FullName;
    }

    public static string OutputDirectory() => Path.Combine(FindTaskRoot(), "output");

    public static string SubmissionFilePath() => Path.Combine(FindTaskRoot(), "submission.md");
}
