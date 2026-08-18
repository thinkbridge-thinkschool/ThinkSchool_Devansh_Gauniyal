using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Day8Task2.Verification;

public class KeyLookupProofTests
{
    private static readonly XNamespace ShowPlanNs = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    // A genuine Key Lookup shows up in showplan XML as a RelOp whose
    // PhysicalOp is "Clustered Index Seek", carrying a nested descriptor
    // element (IndexScan, in the schema version this SQL Server build
    // emits) with the attribute Lookup="1" -- that attribute is the actual
    // element SQL Server uses to mark a bookmark lookup, so it's checked
    // directly against parsed elements rather than string-matching the
    // whole file. Older/estimated-only plans sometimes represent this
    // instead via a RelOp literally named PhysicalOp="Key Lookup", which
    // this also checks for.
    private static bool PlanContainsKeyLookup(string stage)
    {
        var path = Paths.StageFile(stage, "query_plan.sqlplan");
        var doc = XDocument.Parse(File.ReadAllText(path));

        bool anyLookupAttribute = doc.Descendants().Any(e => (string?)e.Attribute("Lookup") == "1");
        bool anyKeyLookupPhysicalOp = doc.Descendants(ShowPlanNs + "RelOp").Any(relOp =>
            string.Equals((string?)relOp.Attribute("PhysicalOp"), "Key Lookup", StringComparison.OrdinalIgnoreCase));

        return anyLookupAttribute || anyKeyLookupPhysicalOp;
    }

    private static int TotalLogicalReads(string stage)
    {
        var path = Paths.StageFile(stage, "query_stats_profile.txt");
        var text = File.ReadAllText(path);

        var matches = Regex.Matches(text, @"logical reads\s+(\d+)", RegexOptions.IgnoreCase);
        Assert.True(matches.Count > 0, $"No 'logical reads' figures found in {stage}/query_stats_profile.txt");

        return matches.Select(m => int.Parse(m.Groups[1].Value)).Sum();
    }

    [Fact]
    public void Stage1_before_plan_genuinely_contains_a_key_lookup()
    {
        Assert.True(PlanContainsKeyLookup("stage1-before"),
            "Expected stage1-before's actual plan to contain a Key Lookup (RelOp with Lookup=\"1\" or PhysicalOp=\"Key Lookup\"), but none was found.");
    }

    [Fact]
    public void Stage2_after_plan_genuinely_has_no_key_lookup()
    {
        Assert.False(PlanContainsKeyLookup("stage2-after"),
            "Expected stage2-after's actual plan to contain NO Key Lookup once the covering index exists, but one was found.");
    }

    [Fact]
    public void Logical_reads_genuinely_drop_once_the_covering_index_exists()
    {
        var before = TotalLogicalReads("stage1-before");
        var after = TotalLogicalReads("stage2-after");

        Assert.True(after < before,
            $"Expected logical reads to drop once the covering index existed, but stage1-before={before} and stage2-after={after}.");
    }
}
