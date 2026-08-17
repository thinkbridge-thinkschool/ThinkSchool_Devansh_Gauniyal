using Xunit;

namespace Task2.Tests;

public class RowNumberVsRankTests
{
    [Fact]
    public void OnATie_RowNumberRankAndDenseRank_DifferExactlyAsExpected()
    {
        using var db = TestDatabase.Create();
        var rows = RowNumberVsRankQuery.Execute(db.Connection)
            .Where(r => r.AuthorName == "Talia Marsh")
            .OrderBy(r => r.RowNum)
            .ToList();

        Assert.Equal(3, rows.Count);

        // Rows 0 and 1 (Quotes 6 and 7) tie on CreatedAt.
        // ROW_NUMBER: always distinct, even on a tie.
        Assert.Equal([1, 2, 3], rows.Select(r => r.RowNum));

        // RANK: both tied rows get rank 1, then the next distinct row SKIPS to rank 3.
        Assert.Equal([1, 1, 3], rows.Select(r => r.Rnk));

        // DENSE_RANK: both tied rows get rank 1, then the next distinct row gets 2 -- no gap.
        Assert.Equal([1, 1, 2], rows.Select(r => r.DenseRnk));
    }
}
