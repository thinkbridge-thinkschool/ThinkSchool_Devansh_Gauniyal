using System.Text.RegularExpressions;
using Xunit;

namespace Day8Task1.Verification;

public class SubmissionAndSecrecyTests
{
    [Fact]
    public void Submission_file_exists_with_all_four_required_headings()
    {
        var path = Path.Combine(Paths.FindTaskRoot(), "submission.md");
        Assert.True(File.Exists(path), "submission.md is missing");

        var text = File.ReadAllText(path);
        Assert.Contains("## GitHub link", text);
        Assert.Contains("## Notes for mentor", text);
        Assert.Contains("## What did you learn this session?", text);
        Assert.Contains("## What would break this?", text);
    }

    private static readonly Regex[] SecretPatterns =
    [
        new(@"MSSQL_SA_PASSWORD\s*=\s*[^\s""']{4,}", RegexOptions.IgnoreCase),
        new(@"-P\s+[""']?(?!\$)[^\s""']{6,}", RegexOptions.None), // sqlcmd -P <literal password>, not -P "$VAR"
        new(@"\bPassword\s*=\s*[^;\s]{4,}", RegexOptions.IgnoreCase), // connection-string style
        new(@"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}"), // JWT-shaped token
    ];

    public static IEnumerable<object[]> AllTrackedFiles()
    {
        var root = Paths.FindTaskRoot();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            // This test project's own source is hand-authored, never generated
            // from a real run, and its secret-detection regex literals
            // otherwise match themselves as text. Real captured secrets can
            // only ever land in sql/, scripts/, output/, README.md or
            // submission.md, so those are the files worth scanning.
            if (file.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}"))
                continue;

            yield return new object[] { Path.GetRelativePath(root, file) };
        }
    }

    [Theory]
    [MemberData(nameof(AllTrackedFiles))]
    public void No_file_contains_a_password_token_or_credentialed_connection_string(string relativePath)
    {
        var root = Paths.FindTaskRoot();
        var fullPath = Path.Combine(root, relativePath);

        byte[] bytes = File.ReadAllBytes(fullPath);
        // Skip anything that isn't plain text (e.g. a future binary asset).
        if (bytes.Take(4096).Any(b => b == 0))
            return;

        var text = File.ReadAllText(fullPath);

        foreach (var pattern in SecretPatterns)
        {
            Assert.False(pattern.IsMatch(text), $"{relativePath} appears to contain a secret matching /{pattern}/");
        }
    }
}
