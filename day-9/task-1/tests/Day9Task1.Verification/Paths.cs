namespace Day9Task1.Verification;

internal static class Paths
{
    public static readonly string SqlDirectory = Path.Combine(AppContext.BaseDirectory, "sql");
    public static readonly string OutputDirectory = Path.Combine(AppContext.BaseDirectory, "output");

    public static string Sql(string fileName) => Path.Combine(SqlDirectory, fileName);

    public static string RunDir(string anomaly, string tag) => Path.Combine(OutputDirectory, anomaly, tag);

    public static string Transcript(string anomaly, string tag, string session) =>
        Path.Combine(RunDir(anomaly, tag), $"{session}.transcript.txt");

    public static string Rendered(string anomaly, string tag, string session) =>
        Path.Combine(RunDir(anomaly, tag), $"{session}.rendered.sql");

    public static string Spids(string anomaly, string tag) => Path.Combine(RunDir(anomaly, tag), "spids.txt");

    public static string SnapshotSettings => Path.Combine(OutputDirectory, "00_snapshot_settings.txt");

    // Walk up from the test assembly's location (deep under
    // tests/Day9Task1.Verification/bin/...) to find the day-9/task-1 root,
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
        throw new DirectoryNotFoundException("Could not locate the day-9/task-1 root from the test assembly location.");
    }
}
