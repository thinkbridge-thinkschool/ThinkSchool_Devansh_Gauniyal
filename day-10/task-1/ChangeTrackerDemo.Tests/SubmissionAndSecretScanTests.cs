using ChangeTrackerDemo;
using Xunit;

namespace ChangeTrackerDemo.Tests;

public class SubmissionFileTests
{
    [Fact]
    public void SubmissionMarkdown_Exists_AndHasAllRequiredHeadings()
    {
        string path = TaskPaths.SubmissionFilePath();
        Assert.True(File.Exists(path), $"submission.md not found at {path}");

        string content = File.ReadAllText(path);
        Assert.Contains("## GitHub link", content);
        Assert.Contains("## Notes for mentor", content);
        Assert.Contains("## What did you learn this session?", content);
        Assert.Contains("## What would break this?", content);
    }
}

// No file under day-10/task-1 (excluding build output) may contain a password, token,
// or a connection string with embedded credentials. The SQLite connection string used
// throughout this task is a bare local file path, so this should always pass.
public class SecretScanTests
{
    private static readonly string[] ForbiddenPatterns =
    {
        "password=",
        "pwd=",
        "Server=tcp:",
        "AccountKey=",
        "-----BEGIN",
    };

    [Fact]
    public void NoFileUnderTaskRoot_ContainsACredentialLookingPattern()
    {
        string root = TaskPaths.FindTaskRoot();
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     // this file itself must declare the forbidden patterns as literal strings to scan for them
                     && Path.GetFileName(f) != "SubmissionAndSecretScanTests.cs");

        foreach (var file in files)
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var pattern in ForbiddenPatterns)
            {
                Assert.False(
                    text.Contains(pattern, StringComparison.OrdinalIgnoreCase),
                    $"File {file} appears to contain a credential-looking pattern: {pattern}");
            }
        }
    }
}
