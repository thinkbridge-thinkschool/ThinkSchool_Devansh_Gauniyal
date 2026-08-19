namespace Day9Task2.Verification;

internal static class Paths
{
    public static readonly string SqlDirectory = Path.Combine(AppContext.BaseDirectory, "sql");
    public static readonly string OutputDirectory = Path.Combine(AppContext.BaseDirectory, "output");

    public static string Sql(string fileName) => Path.Combine(SqlDirectory, fileName);

    public static string RunDir(string name) => Path.Combine(OutputDirectory, name);

    public static string Transcript(string name, string session) =>
        Path.Combine(RunDir(name), $"{session}.transcript.txt");

    public static string Rendered(string name, string session) =>
        Path.Combine(RunDir(name), $"{session}.rendered.sql");

    public static string Spids(string name) => Path.Combine(RunDir(name), "spids.txt");

    public static string Victim(string name) => Path.Combine(RunDir(name), "victim.txt");

    public static string DeadlockGraphXdl => Path.Combine(OutputDirectory, "deadlock_graph.xdl");

    public static string ErrorLogDeadlockReport => Path.Combine(OutputDirectory, "errorlog_deadlock_report.txt");

    public static string TraceFlagEnabledCapture => Path.Combine(OutputDirectory, "traceflag_1222_enabled.txt");

    public static string TraceFlagDisabledCapture => Path.Combine(OutputDirectory, "traceflag_1222_disabled.txt");

    // Walk up from the test assembly's location (deep under
    // tests/Day9Task2.Verification/bin/...) to find the day-9/task-2 root,
    // identified by having sibling sql/, scripts/ and tests/ directories.
    // Used only for the whole-tree secret scan and submission.md check, which
    // must see the real source tree (README.md, scripts/, submission.md),
    // not just the copied test-output items.
    public static string FindTaskRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "sql")) &&
                Directory.Exists(Path.Combine(dir.FullName, "scripts")) &&
                Directory.Exists(Path.Combine(dir.FullName, "tests")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the day-9/task-2 root from the test assembly location.");
    }
}
