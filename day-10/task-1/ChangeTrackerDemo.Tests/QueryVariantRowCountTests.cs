using ChangeTrackerDemo;
using Xunit;

namespace ChangeTrackerDemo.Tests;

public class QueryVariantRowCountTests : IClassFixture<SharedCatalogFixture>
{
    private readonly SharedCatalogFixture _fixture;

    public QueryVariantRowCountTests(SharedCatalogFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void TrackedAndNoTracking_ReturnSameRowCount_TenThousand()
    {
        using var trackedContext = new CatalogContext(_fixture.DbPath);
        using var noTrackingContext = new CatalogContext(_fixture.DbPath);

        var tracked = QueryVariants.ReadAllTracked(trackedContext);
        var noTracking = QueryVariants.ReadAllNoTracking(noTrackingContext);

        Assert.Equal(10_000, tracked.Count);
        Assert.Equal(10_000, noTracking.Count);
        Assert.Equal(tracked.Count, noTracking.Count);
    }
}
