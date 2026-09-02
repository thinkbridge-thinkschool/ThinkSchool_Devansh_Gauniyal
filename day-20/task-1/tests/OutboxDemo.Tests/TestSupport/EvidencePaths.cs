namespace OutboxDemo.Tests.TestSupport;

/// <summary>
/// Locates day-20/task-1/output by walking up from the test binary's own
/// directory to the solution file, so evidence never lands outside the task
/// folder regardless of build configuration.
/// </summary>
public static class EvidencePaths
{
    public static string OutputDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Outbox.slnx")))
            {
                dir = dir.Parent;
            }

            if (dir is null)
            {
                throw new InvalidOperationException(
                    $"Could not locate Outbox.slnx above {AppContext.BaseDirectory}");
            }

            var output = Path.Combine(dir.FullName, "output");
            Directory.CreateDirectory(output);
            return output;
        }
    }
}
