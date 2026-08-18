using System.Text.RegularExpressions;
using Xunit;

namespace Day8Task1.Verification;

public class LogicalReadsImprovementTests
{
    // Total logical reads for a statement, summed across every "Table '...'."
    // line SET STATISTICS IO produced (a plan with a Key Lookup can touch the
    // base table more than once).
    private static int TotalLogicalReads(string stage, int query)
    {
        var path = Paths.StageFile(stage, $"q{query}_stats_profile.txt");
        var text = File.ReadAllText(path);

        var matches = Regex.Matches(text, @"logical reads\s+(\d+)", RegexOptions.IgnoreCase);
        Assert.True(matches.Count > 0, $"No 'logical reads' figures found in {stage}/q{query}_stats_profile.txt");

        return matches.Select(m => int.Parse(m.Groups[1].Value)).Sum();
    }

    // Q1 targets the clustered index (10_clustered_index.sql): heap -> clustered.
    [Fact]
    public void Q1_logical_reads_drop_once_the_clustered_index_exists()
    {
        var before = TotalLogicalReads("stage0-heap", 1);
        var after = TotalLogicalReads("stage1-clustered", 1);

        Assert.True(after < before,
            $"Expected Q1 logical reads to drop after the clustered index was added, " +
            $"but stage0-heap={before} and stage1-clustered={after}.");
    }

    // Q2 targets the plain nonclustered index on CustomerId (11_nonclustered_customer.sql).
    [Fact]
    public void Q2_logical_reads_drop_once_the_nonclustered_customer_index_exists()
    {
        var before = TotalLogicalReads("stage1-clustered", 2);
        var after = TotalLogicalReads("stage2-nc-customer", 2);

        Assert.True(after < before,
            $"Expected Q2 logical reads to drop after IX_Orders_CustomerId was added, " +
            $"but stage1-clustered={before} and stage2-nc-customer={after}.");
    }

    // Q3 targets the covering nonclustered index (12_nonclustered_covering.sql).
    [Fact]
    public void Q3_logical_reads_drop_once_the_covering_index_exists()
    {
        var before = TotalLogicalReads("stage2-nc-customer", 3);
        var after = TotalLogicalReads("stage3-nc-covering", 3);

        Assert.True(after < before,
            $"Expected Q3 logical reads to drop after IX_Orders_CustomerId_Covering was added, " +
            $"but stage2-nc-customer={before} and stage3-nc-covering={after}.");
    }
}
