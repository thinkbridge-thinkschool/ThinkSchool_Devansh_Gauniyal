using QueryTranslationDemo;
using Xunit;

namespace QueryTranslationDemo.Tests;

[Collection("QueryEvidence")]
public class DemonstrationTests
{
    private readonly QueryEvidenceFixture _fixture;

    public DemonstrationTests(QueryEvidenceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void BeforeQuery_LoggedSqlIsCaptured_AndNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(_fixture.Report.Before.RawSql));
    }

    [Fact]
    public void AfterQuery_LoggedSqlIsCaptured_AndNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(_fixture.Report.After.RawSql));
    }

    [Fact]
    public void BeforeAndAfter_ReturnSameRowCount()
    {
        Assert.True(_fixture.Report.Before.RowCount > 0);
        Assert.Equal(_fixture.Report.Before.RowCount, _fixture.Report.After.RowCount);
    }

    [Fact]
    public void BrokenQuery_ThrowsInvalidOperationException_AboutTranslation()
    {
        Assert.Equal(typeof(InvalidOperationException).FullName, _fixture.Report.Broken.ExceptionType);
        Assert.Contains(
            "could not be translated",
            _fixture.Report.Broken.ExceptionMessage,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FixedQuery_ExecutesWithoutThrowing_AndReturnsExpectedRows()
    {
        Assert.False(string.IsNullOrWhiteSpace(_fixture.Report.FixedQuery.RawSql));
        Assert.True(_fixture.Report.FixedQuery.RowCount > 0);
    }

    [Fact]
    public void AsEnumerableVariant_ReturnsSameLogicalResult_AndPulledAllRowsFirst()
    {
        // Same "PREMIUM" predicate as the FIXED query, so the two should agree on rows -
        // proving the untranslatable and translatable versions filter the same set.
        Assert.Equal(_fixture.Report.FixedQuery.RowCount, _fixture.Report.AsEnumerableVariant.RowCountAfterFilter);

        // AsEnumerable() ran before Where, so every seeded row was pulled into memory
        // first, then filtered client-side.
        Assert.Equal(Seeder.ProductCount, _fixture.Report.AsEnumerableVariant.RowsPulledIntoMemory);
    }
}
