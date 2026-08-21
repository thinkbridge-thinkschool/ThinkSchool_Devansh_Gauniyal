namespace FastApi.Tests;

// Locates day-11/task-2 on disk by walking up from wherever this test assembly is
// actually running, plus the sibling day-11/task-1 folder that holds the "before" baseline
// evidence this task's comparison depends on.
public static class TaskPaths
{
    public static string FindTaskRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Task2.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not locate day-11/task-2 root (no Task2.slnx found in any parent directory).");
        }

        return dir.FullName;
    }

    public static string OutputDirectory() => Path.Combine(FindTaskRoot(), "output");

    public static string SubmissionFilePath() => Path.Combine(FindTaskRoot(), "submission.md");

    // Sibling day-11/task-1 folder - read-only, never written to. Holds the committed
    // "before" baseline this task's re-measurement is compared against.
    public static string Task1OutputDirectory()
    {
        var day11Dir = Directory.GetParent(FindTaskRoot())
            ?? throw new InvalidOperationException("Could not locate day-11 parent directory.");
        return Path.Combine(day11Dir.FullName, "task-1", "output");
    }
}
