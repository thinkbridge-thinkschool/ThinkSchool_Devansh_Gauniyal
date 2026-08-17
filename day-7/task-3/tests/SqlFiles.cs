namespace Task3.Tests;

internal static class SqlFiles
{
    private static readonly string SqlDirectory = Path.Combine(AppContext.BaseDirectory, "sql");

    public static string Read(string fileName) => File.ReadAllText(Path.Combine(SqlDirectory, fileName));
}
