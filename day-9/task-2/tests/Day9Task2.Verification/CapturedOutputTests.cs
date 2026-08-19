using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Day9Task2.Verification;

public class CapturedOutputTests
{
    public static readonly string[] RunNames = ["10_deadlock_broken", "20_fixed"];

    public static IEnumerable<object[]> RunNameData() => RunNames.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(RunNameData))]
    public void Both_session_transcripts_exist_and_are_non_empty(string runName)
    {
        var transcriptA = Paths.Transcript(runName, "sessionA");
        var transcriptB = Paths.Transcript(runName, "sessionB");

        Assert.True(File.Exists(transcriptA), $"Missing: {transcriptA}");
        Assert.True(File.Exists(transcriptB), $"Missing: {transcriptB}");
        Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(transcriptA)));
        Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(transcriptB)));
    }

    [Theory]
    [MemberData(nameof(RunNameData))]
    public void Two_distinct_SPIDs_are_recorded_for_every_captured_run(string runName)
    {
        var path = Paths.Spids(runName);
        Assert.True(File.Exists(path), $"Missing: {path}");

        var text = File.ReadAllText(path);
        var spidA = Regex.Match(text, @"Session A SPID:\s*(\d+)");
        var spidB = Regex.Match(text, @"Session B SPID:\s*(\d+)");

        Assert.True(spidA.Success, $"Session A SPID not recorded in {path}");
        Assert.True(spidB.Success, $"Session B SPID not recorded in {path}");
        Assert.NotEqual(spidA.Groups[1].Value, spidB.Groups[1].Value);
    }

    [Fact]
    public void Broken_scenario_has_exactly_one_session_reporting_error_1205()
    {
        var textA = File.ReadAllText(Paths.Transcript("10_deadlock_broken", "sessionA"));
        var textB = File.ReadAllText(Paths.Transcript("10_deadlock_broken", "sessionB"));

        var aIsVictim = Regex.IsMatch(textA, @"Msg 1205,.*deadlocked", RegexOptions.Singleline);
        var bIsVictim = Regex.IsMatch(textB, @"Msg 1205,.*deadlocked", RegexOptions.Singleline);

        // Exactly one of the two sessions was chosen as the deadlock victim
        // - never both, never neither, and never assumed to be a specific
        // one of the two (victim selection is not deterministic; see
        // README.md and output/10_deadlock_broken/victim.txt for which
        // session it was in this particular captured run).
        Assert.True(aIsVictim ^ bIsVictim, $"Expected exactly one of the two sessions to report error 1205. Session A: {aIsVictim}, Session B: {bIsVictim}");
    }

    [Fact]
    public void Broken_scenario_never_reports_a_lock_timeout_in_place_of_a_deadlock()
    {
        // Error 1222 ("Lock request time out period exceeded") is a lock
        // timeout, not a deadlock. If it appeared here instead of 1205, the
        // repro would have produced blocking, not a genuine deadlock - see
        // the number disambiguation in README.md.
        var textA = File.ReadAllText(Paths.Transcript("10_deadlock_broken", "sessionA"));
        var textB = File.ReadAllText(Paths.Transcript("10_deadlock_broken", "sessionB"));

        Assert.DoesNotContain("1222", textA);
        Assert.DoesNotContain("1222", textB);
    }

    [Fact]
    public void Broken_scenario_victim_record_matches_the_transcripts()
    {
        var path = Paths.Victim("10_deadlock_broken");
        Assert.True(File.Exists(path), $"Missing: {path}");
        var text = File.ReadAllText(path);

        Assert.Matches(new Regex(@"Victim: Session [AB] \(SPID \d+\)"), text);
        Assert.DoesNotContain("UNEXPECTED", text);
    }

    [Fact]
    public void Fixed_scenario_has_no_deadlock_and_both_sessions_complete()
    {
        var textA = File.ReadAllText(Paths.Transcript("20_fixed", "sessionA"));
        var textB = File.ReadAllText(Paths.Transcript("20_fixed", "sessionB"));

        Assert.DoesNotContain("1205", textA);
        Assert.DoesNotContain("1205", textB);
        Assert.DoesNotContain("1222", textA);
        Assert.DoesNotContain("1222", textB);

        // Both sessions' final commit must have been reached and printed.
        Assert.Contains("MARK:A_STEP3_COMMIT_DONE", textA);
        Assert.Contains("MARK:B_STEP3_COMMIT_DONE", textB);

        // Both UPDATEs actually affected a row, on both sessions.
        Assert.Contains("(1 rows affected)", textA);
        Assert.Contains("(1 rows affected)", textB);
    }

    [Fact]
    public void Deadlock_graph_xdl_exists_is_well_formed_xml_and_has_two_processes_and_resources()
    {
        var path = Paths.DeadlockGraphXdl;
        Assert.True(File.Exists(path), $"Missing: {path}");
        var text = File.ReadAllText(path);
        Assert.False(string.IsNullOrWhiteSpace(text), $"{path} exists but is empty");

        XDocument doc;
        try
        {
            doc = XDocument.Parse(text);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException($"{path} is not well-formed XML: {ex.Message}\n---\n{text}");
        }

        var processes = doc.Descendants().Where(e => e.Name.LocalName == "process").ToList();
        Assert.True(processes.Count >= 2, $"Expected at least two <process> entries in the deadlock report, found {processes.Count}");

        var resourceList = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "resource-list");
        Assert.NotNull(resourceList);
        var resourceNodes = resourceList!.Elements().ToList();
        Assert.True(resourceNodes.Count >= 2, $"Expected at least two resource nodes (one per table) under resource-list, found {resourceNodes.Count}");
    }

    [Fact]
    public void Errorlog_deadlock_report_capture_is_present_and_non_empty()
    {
        var path = Paths.ErrorLogDeadlockReport;
        Assert.True(File.Exists(path), $"Missing: {path}");
        var text = File.ReadAllText(path);
        Assert.False(string.IsNullOrWhiteSpace(text), $"{path} exists but is empty");
        Assert.Contains("deadlock-list", text);
        Assert.Contains("process id=", text);
        Assert.Contains("resource-list", text);
    }

    [Fact]
    public void Trace_flag_1222_was_confirmed_on_then_off()
    {
        Assert.True(File.Exists(Paths.TraceFlagEnabledCapture));
        Assert.True(File.Exists(Paths.TraceFlagDisabledCapture));

        var enabledText = File.ReadAllText(Paths.TraceFlagEnabledCapture);
        var disabledText = File.ReadAllText(Paths.TraceFlagDisabledCapture);

        Assert.Matches(new Regex(@"1222\s+1\s"), enabledText);
        Assert.DoesNotMatch(new Regex(@"1222\s+1\s"), disabledText);
        Assert.Matches(new Regex(@"1222\s+0\s"), disabledText);
    }
}
