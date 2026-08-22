using System.Text.RegularExpressions;

namespace CqrsLite.Tests;

public class SecretScanTests
{
    private static readonly string[] ScanExtensions =
        { ".cs", ".csproj", ".slnx", ".md", ".sh", ".json", ".txt", ".log" };

    private static readonly (string Name, Regex Pattern)[] SecretPatterns =
    {
        ("password assignment", new Regex(@"password\s*=\s*[^=\s]", RegexOptions.IgnoreCase)),
        ("connection string with credentials", new Regex(@"(User Id|Uid)\s*=.*Password\s*=", RegexOptions.IgnoreCase)),
        ("AWS access key", new Regex(@"AKIA[0-9A-Z]{16}")),
        ("GitHub token", new Regex(@"gh[pousr]_[A-Za-z0-9]{20,}")),
        ("private key block", new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----")),
        ("JWT-looking token", new Regex(@"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}")),
    };

    [Fact]
    public void No_file_under_this_task_contains_a_credential_pattern()
    {
        var root = TaskPaths.FindTaskRoot();
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => ScanExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

        var violations = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var (name, pattern) in SecretPatterns)
            {
                if (pattern.IsMatch(text))
                {
                    violations.Add($"{file}: matched '{name}'");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void No_file_under_this_task_contains_a_sqlite_connection_string_with_a_real_path_outside_this_task()
    {
        // The only connection string shape this project ever builds is "Data Source={path}"
        // where {path} is a local SQLite file - never a server, user, or password. This just
        // confirms no other connection-string shape crept in anywhere under this task.
        var root = TaskPaths.FindTaskRoot();
        var csFiles = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var serverConnectionPattern = new Regex(@"Server\s*=|Data Source\s*=\s*[\w.]+,\d+", RegexOptions.IgnoreCase);

        foreach (var file in csFiles)
        {
            var text = File.ReadAllText(file);
            Assert.False(serverConnectionPattern.IsMatch(text), $"{file} appears to contain a server-style connection string.");
        }
    }
}
