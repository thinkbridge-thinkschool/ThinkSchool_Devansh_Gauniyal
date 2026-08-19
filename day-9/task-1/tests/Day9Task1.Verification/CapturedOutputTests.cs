using System.Text.RegularExpressions;
using Xunit;

namespace Day9Task1.Verification;

public class CapturedOutputTests
{
    public static readonly (string Anomaly, string Tag)[] AllRuns =
    [
        ("10_dirty_read", "occurs_READ_UNCOMMITTED"),
        ("10_dirty_read", "prevented_READ_COMMITTED"),
        ("11_nonrepeatable", "occurs_READ_COMMITTED"),
        ("11_nonrepeatable", "prevented_REPEATABLE_READ"),
        ("12_phantom", "occurs_REPEATABLE_READ"),
        ("12_phantom", "prevented_SERIALIZABLE"),
    ];

    public static IEnumerable<object[]> AllRunData() => AllRuns.Select(r => new object[] { r.Anomaly, r.Tag });

    [Theory]
    [MemberData(nameof(AllRunData))]
    public void Both_session_transcripts_exist_and_are_non_empty_for_every_captured_run(string anomaly, string tag)
    {
        var transcriptA = Paths.Transcript(anomaly, tag, "sessionA");
        var transcriptB = Paths.Transcript(anomaly, tag, "sessionB");

        Assert.True(File.Exists(transcriptA), $"Missing: {transcriptA}");
        Assert.True(File.Exists(transcriptB), $"Missing: {transcriptB}");
        Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(transcriptA)));
        Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(transcriptB)));
    }

    [Theory]
    [MemberData(nameof(AllRunData))]
    public void Two_distinct_SPIDs_are_recorded_for_every_captured_run(string anomaly, string tag)
    {
        var path = Paths.Spids(anomaly, tag);
        Assert.True(File.Exists(path), $"Missing: {path}");

        var text = File.ReadAllText(path);
        var spidA = Regex.Match(text, @"Session A SPID:\s*(\d+)");
        var spidB = Regex.Match(text, @"Session B SPID:\s*(\d+)");

        Assert.True(spidA.Success, $"Session A SPID not recorded in {path}");
        Assert.True(spidB.Success, $"Session B SPID not recorded in {path}");
        Assert.NotEqual(spidA.Groups[1].Value, spidB.Groups[1].Value);
    }

    [Fact]
    public void Snapshot_settings_were_recorded_and_are_both_off()
    {
        Assert.True(File.Exists(Paths.SnapshotSettings), $"Missing: {Paths.SnapshotSettings}");
        var text = File.ReadAllText(Paths.SnapshotSettings);

        var row = Regex.Match(text, @"IsolationLab\s+(\d)\s+(\w+)");
        Assert.True(row.Success, $"Could not find the IsolationLab row in {Paths.SnapshotSettings}:\n{text}");

        Assert.Equal("0", row.Groups[1].Value); // ReadCommittedSnapshotOn
        Assert.Equal("OFF", row.Groups[2].Value); // AllowSnapshotIsolationState
    }

    // Returns the transcript text strictly between two literal "MARK:<name>"
    // markers the orchestrator prints around each step, so a test reads
    // exactly what one specific step produced rather than guessing at
    // output-format offsets.
    private static string Section(string transcript, string startMark, string endMark)
    {
        var startToken = $"MARK:{startMark}";
        var endToken = $"MARK:{endMark}";
        var startIdx = transcript.IndexOf(startToken, StringComparison.Ordinal);
        var endIdx = transcript.IndexOf(endToken, StringComparison.Ordinal);

        Assert.True(startIdx >= 0, $"Marker {startToken} not found in transcript");
        Assert.True(endIdx >= 0, $"Marker {endToken} not found in transcript");
        Assert.True(endIdx > startIdx, $"Marker {endToken} appears before {startToken}");

        return transcript[(startIdx + startToken.Length)..endIdx];
    }

    private static string ExtractBalance(string section, int id)
    {
        var m = Regex.Match(section, $@"^\s*{id}\s+(\d+\.\d{{2}})\s*$", RegexOptions.Multiline);
        Assert.True(m.Success, $"Could not find a Balance value for Id {id} in section:\n{section}");
        return m.Groups[1].Value;
    }

    private static int ExtractRowCount(string section)
    {
        var m = Regex.Match(section, @"\((\d+)\s+rows?\s+affected\)");
        Assert.True(m.Success, $"Could not find a row-count message in section:\n{section}");
        return int.Parse(m.Groups[1].Value);
    }

    [Fact]
    public void Dirty_read_occurs_under_READ_UNCOMMITTED_and_the_value_is_later_rolled_back()
    {
        var text = File.ReadAllText(Paths.Transcript("10_dirty_read", "occurs_READ_UNCOMMITTED", "sessionB"));

        var dirtyValue = ExtractBalance(Section(text, "B_STEP2_READ_STARTING", "B_STEP2_READ_DONE"), 2);
        var postRollbackValue = ExtractBalance(Section(text, "B_STEP2_READ_DONE", "B_STEP3_READ_DONE"), 2);

        Assert.Equal("9999.99", dirtyValue);
        Assert.Equal("1500.00", postRollbackValue);
        Assert.NotEqual(dirtyValue, postRollbackValue);
    }

    [Fact]
    public void Dirty_read_is_prevented_under_READ_COMMITTED()
    {
        var text = File.ReadAllText(Paths.Transcript("10_dirty_read", "prevented_READ_COMMITTED", "sessionB"));

        var attempt = Section(text, "B_STEP2_READ_STARTING", "B_STEP2_READ_DONE");
        var postRollbackValue = ExtractBalance(Section(text, "B_STEP2_READ_DONE", "B_STEP3_READ_DONE"), 2);

        // The dirty (uncommitted) value must never appear in the attempt
        // section - proof requires either a consistent read matching the
        // post-rollback value, or a genuine lock timeout (error 1222).
        Assert.DoesNotContain("9999.99", attempt);
        Assert.Equal("1500.00", postRollbackValue);
        var consistentRead = attempt.Contains("1500.00");
        var timedOut = attempt.Contains("1222");
        Assert.True(consistentRead || timedOut, $"Expected a consistent read or a 1222 lock timeout as evidence of prevention. Section was:\n{attempt}");
    }

    [Fact]
    public void Nonrepeatable_read_occurs_under_READ_COMMITTED()
    {
        var text = File.ReadAllText(Paths.Transcript("11_nonrepeatable", "occurs_READ_COMMITTED", "sessionB"));

        var firstRead = ExtractBalance(Section(text, "B_ISOLATION_SET", "B_STEP2_FIRST_READ_DONE"), 1);
        var secondRead = ExtractBalance(Section(text, "B_STEP2_FIRST_READ_DONE", "B_STEP3_SECOND_READ_DONE"), 1);

        Assert.Equal("1000.00", firstRead);
        Assert.Equal("1111.11", secondRead);
        Assert.NotEqual(firstRead, secondRead);
    }

    [Fact]
    public void Nonrepeatable_read_is_prevented_under_REPEATABLE_READ()
    {
        var textB = File.ReadAllText(Paths.Transcript("11_nonrepeatable", "prevented_REPEATABLE_READ", "sessionB"));

        var firstRead = ExtractBalance(Section(textB, "B_ISOLATION_SET", "B_STEP2_FIRST_READ_DONE"), 1);
        var secondRead = ExtractBalance(Section(textB, "B_STEP2_FIRST_READ_DONE", "B_STEP3_SECOND_READ_DONE"), 1);
        Assert.Equal(firstRead, secondRead);

        // The writer's UPDATE must have genuinely been blocked, not simply
        // never attempted: this orchestrator always waits past the 5s
        // LOCK_TIMEOUT before letting Session B commit (see
        // run-experiment.sh), so a real 1222 is the expected outcome here.
        var textA = File.ReadAllText(Paths.Transcript("11_nonrepeatable", "prevented_REPEATABLE_READ", "sessionA"));
        Assert.Contains("1222", textA);
        Assert.Contains("Lock request time out period exceeded", textA);
    }

    [Fact]
    public void Phantom_read_occurs_under_REPEATABLE_READ()
    {
        var text = File.ReadAllText(Paths.Transcript("12_phantom", "occurs_REPEATABLE_READ", "sessionB"));

        var firstCount = ExtractRowCount(Section(text, "B_ISOLATION_SET", "B_STEP2_FIRST_RANGE_DONE"));
        var secondCount = ExtractRowCount(Section(text, "B_STEP2_FIRST_RANGE_DONE", "B_STEP3_SECOND_RANGE_DONE"));

        Assert.Equal(3, firstCount);
        Assert.Equal(4, secondCount);
        Assert.True(secondCount > firstCount, "Expected the second range read to return more rows than the first (the phantom).");
    }

    [Fact]
    public void Phantom_read_is_prevented_under_SERIALIZABLE()
    {
        var textB = File.ReadAllText(Paths.Transcript("12_phantom", "prevented_SERIALIZABLE", "sessionB"));

        var firstCount = ExtractRowCount(Section(textB, "B_ISOLATION_SET", "B_STEP2_FIRST_RANGE_DONE"));
        var secondCount = ExtractRowCount(Section(textB, "B_STEP2_FIRST_RANGE_DONE", "B_STEP3_SECOND_RANGE_DONE"));
        Assert.Equal(firstCount, secondCount);

        var textA = File.ReadAllText(Paths.Transcript("12_phantom", "prevented_SERIALIZABLE", "sessionA"));
        Assert.Contains("1222", textA);
        Assert.Contains("Lock request time out period exceeded", textA);
    }
}
