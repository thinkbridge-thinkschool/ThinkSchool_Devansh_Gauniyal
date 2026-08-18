namespace Day8Task1.Verification;

internal static class Paths
{
    public static readonly string SqlDirectory = Path.Combine(AppContext.BaseDirectory, "sql");
    public static readonly string OutputDirectory = Path.Combine(AppContext.BaseDirectory, "output");

    public static string Sql(string fileName) => Path.Combine(SqlDirectory, fileName);

    public static string StageFile(string stage, string fileName) => Path.Combine(OutputDirectory, stage, fileName);

    // Walk up from the test assembly's location (deep under
    // tests/Day8Task1.Verification/bin/...) to find the day-8/task-1 root,
    // identified by having sibling sql/, scripts/ and tests/ directories.
    // Used only for the whole-tree secret scan, which must see the real
    // source tree (README.md, scripts/), not just the copied test-output items.
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
        throw new DirectoryNotFoundException("Could not locate the day-8/task-1 root from the test assembly location.");
    }
}
