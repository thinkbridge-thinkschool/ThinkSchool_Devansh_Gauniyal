namespace DapperComparison.Tests;

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
                "Could not locate day-12/task-2 root (no Task2.slnx found in any parent directory).");
        }

        return dir.FullName;
    }

    public static string OutputDirectory() => Path.Combine(FindTaskRoot(), "output");

    public static string SubmissionFilePath() => Path.Combine(FindTaskRoot(), "submission.md");

    public static string SourceFile(params string[] relativeParts)
    {
        var parts = new[] { FindTaskRoot(), "DapperComparison" }.Concat(relativeParts).ToArray();
        return Path.Combine(parts);
    }
}
