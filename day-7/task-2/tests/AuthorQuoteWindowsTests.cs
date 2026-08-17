using Xunit;

namespace Task2.Tests;

public class AuthorQuoteWindowsTests
{
    [Fact]
    public void FirstQuotePerAuthor_HasNullGap_NotZero()
    {
        using var db = TestDatabase.Create();
        var rows = AuthorQuoteWindowsQuery.Execute(db.Connection);

        var dorianFirst = Assert.Single(rows, r => r.AuthorName == "Dorian Fenwick" && r.RunningQuoteCount == 1);
        Assert.Null(dorianFirst.PreviousQuoteCreatedAt);
        Assert.Null(dorianFirst.GapDaysRaw);
        Assert.Null(dorianFirst.GapDaysRounded);

        var wrenFirst = Assert.Single(rows, r => r.AuthorName == "Wren Ashby" && r.RunningQuoteCount == 1);
        Assert.Null(wrenFirst.GapDaysRaw);
        Assert.Null(wrenFirst.GapDaysRounded);
    }

    [Fact]
    public void RunningCount_IncrementsSequentially_AndFinalValueEqualsTotalQuoteCount()
    {
        using var db = TestDatabase.Create();

        using var countCmd = db.Connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM Quotes q JOIN Authors a ON a.Id = q.AuthorId WHERE a.Name = 'Dorian Fenwick';";
        var expectedTotal = Convert.ToInt32(countCmd.ExecuteScalar());

        var rows = AuthorQuoteWindowsQuery.Execute(db.Connection)
            .Where(r => r.AuthorName == "Dorian Fenwick")
            .OrderBy(r => r.RunningQuoteCount)
            .ToList();

        Assert.Equal(expectedTotal, rows.Count);
        Assert.Equal(Enumerable.Range(1, expectedTotal), rows.Select(r => r.RunningQuoteCount));
    }

    [Fact]
    public void SingleQuoteAuthor_YieldsExactlyOneRow_WithRunningCountOne_AndNullGap()
    {
        using var db = TestDatabase.Create();
        var rows = AuthorQuoteWindowsQuery.Execute(db.Connection);

        var callum = Assert.Single(rows, r => r.AuthorName == "Callum Reyes");
        Assert.Equal(1, callum.RunningQuoteCount);
        Assert.Null(callum.PreviousQuoteCreatedAt);
        Assert.Null(callum.GapDaysRaw);
        Assert.Null(callum.GapDaysRounded);
    }

    [Fact]
    public void ZeroQuoteAuthor_YieldsNoRows_BecauseThisQueryIsPerQuoteGrainNotPerAuthor()
    {
        // Unlike Day 7 Task 1's graded query (one row per author, via LEFT JOIN so the
        // zero-quote author still appears with count 0), this query is one row PER QUOTE,
        // built with an INNER JOIN -- an author with no quotes has no row to produce at all,
        // and that absence is the correct, expected behavior for this grain, not a bug.
        using var db = TestDatabase.Create();
        var rows = AuthorQuoteWindowsQuery.Execute(db.Connection);

        Assert.DoesNotContain(rows, r => r.AuthorName == "Nadia Kestrel");
    }

    [Fact]
    public void LargeGap_EqualsIndependentlyComputedDayCount()
    {
        using var db = TestDatabase.Create();
        var rows = AuthorQuoteWindowsQuery.Execute(db.Connection);

        // Wren Ashby's Quote 4 (2024-01-10 08:00:00) follows Quote 3 (2023-06-05 10:00:00).
        // Expected value computed independently via .NET DateTime arithmetic, not derived
        // from the SQL under test.
        var expected = new DateTime(2024, 1, 10, 8, 0, 0) - new DateTime(2023, 6, 5, 10, 0, 0);

        var row = Assert.Single(rows, r =>
            r.AuthorName == "Wren Ashby" && r.CreatedAt == "2024-01-10 08:00:00");

        Assert.NotNull(row.GapDaysRaw);
        Assert.Equal(expected.TotalDays, row.GapDaysRaw!.Value, precision: 6);
        Assert.Equal(Math.Round(expected.TotalDays), row.GapDaysRounded);
    }

    [Fact]
    public void YearBoundaryGap_IsCorrectInDays_NotNegativeFromNaiveDayOfYearSubtraction()
    {
        // Same underlying row as the large-gap test above, but asserting specifically on
        // the year-boundary failure mode: Quote 3 is 2023-06-05 (day-of-year ~156) and
        // Quote 4 is 2024-01-10 (day-of-year 10). A naive "day-of-year minus day-of-year"
        // subtraction would compute 10 - 156 = -146 -- negative and wrong. The real gap,
        // computed via julianday() across the year boundary, is a large POSITIVE number.
        using var db = TestDatabase.Create();
        var rows = AuthorQuoteWindowsQuery.Execute(db.Connection);

        var row = Assert.Single(rows, r =>
            r.AuthorName == "Wren Ashby" && r.CreatedAt == "2024-01-10 08:00:00");

        Assert.NotNull(row.GapDaysRounded);
        Assert.True(row.GapDaysRounded! > 0, "Gap across a year boundary must be positive.");
        Assert.Equal(219.0, row.GapDaysRounded);
    }

    [Fact]
    public void TiedTimestampPair_ProducesZeroGap_WithStableOrderById()
    {
        using var db = TestDatabase.Create();
        var rows = AuthorQuoteWindowsQuery.Execute(db.Connection);

        // Talia Marsh's Quotes 6 and 7 share CreatedAt '2023-04-10 12:00:00'. The Id
        // tie-break in the window's ORDER BY must place Quote 6 (lower Id) first, so
        // Quote 7's running count is 2 and its previous-quote timestamp is Quote 6's
        // identical CreatedAt -- giving a gap of exactly 0, deterministically every run.
        var second = Assert.Single(rows, r =>
            r.AuthorName == "Talia Marsh" && r.RunningQuoteCount == 2);

        Assert.Equal("2023-04-10 12:00:00", second.CreatedAt);
        Assert.Equal("2023-04-10 12:00:00", second.PreviousQuoteCreatedAt);
        Assert.Equal(0.0, second.GapDaysRaw);
        Assert.Equal(0.0, second.GapDaysRounded);
    }
}
