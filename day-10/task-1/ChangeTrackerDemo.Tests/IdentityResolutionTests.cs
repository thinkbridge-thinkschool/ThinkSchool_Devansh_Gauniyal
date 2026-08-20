using ChangeTrackerDemo;
using Xunit;

namespace ChangeTrackerDemo.Tests;

// Demonstration (a): identity resolution. Querying the same primary key twice inside
// ONE tracked DbContext returns the SAME object instance; the same two queries under
// AsNoTracking() return two DIFFERENT instances, because there is no identity map to
// resolve against. These tests deliberately go through the same QueryVariants.ReadAllTracked
// / ReadAllNoTracking methods used by the benchmark, so a mutation to either variant
// (e.g. removing .AsNoTracking()) breaks these tests for a real reason, not vacuously.
public class IdentityResolutionTests
{
    [Fact]
    public void TrackedContext_SameKeyQueriedTwice_ReturnsSameInstance()
    {
        using var db = new TemporaryCatalogDatabase(rowCount: 5);
        using var context = new CatalogContext(db.Path);

        var first = QueryVariants.ReadAllTracked(context).First();
        var second = QueryVariants.ReadAllTracked(context).First();

        Assert.Same(first, second);
    }

    [Fact]
    public void NoTrackingContext_SameKeyQueriedTwice_ReturnsDifferentInstances()
    {
        using var db = new TemporaryCatalogDatabase(rowCount: 5);
        using var context = new CatalogContext(db.Path);

        var first = QueryVariants.ReadAllNoTracking(context).First();
        var second = QueryVariants.ReadAllNoTracking(context).First();

        Assert.NotSame(first, second);
    }

    [Fact]
    public void TrackedContext_SecondQuery_ReturnsInMemoryMutation_ProvingNoReMaterialisation()
    {
        using var db = new TemporaryCatalogDatabase(rowCount: 5);
        using var context = new CatalogContext(db.Path);

        var first = QueryVariants.ReadAllTracked(context).First();
        var originalName = first.Name;
        first.Name = "Mutated-In-Memory-Only";

        var second = QueryVariants.ReadAllTracked(context).First();

        // If the second query had re-read the row from the database, it would come back
        // with the original, unmodified name. Getting the in-memory mutation back proves
        // EF Core returned the already-tracked instance instead of re-materialising it.
        Assert.Same(first, second);
        Assert.Equal("Mutated-In-Memory-Only", second.Name);
        Assert.NotEqual(originalName, second.Name);
    }

    [Fact]
    public void TrackedContext_ChangeTrackerEntries_PopulatedAfterRead()
    {
        using var db = new TemporaryCatalogDatabase(rowCount: 5);
        using var context = new CatalogContext(db.Path);

        _ = QueryVariants.ReadAllTracked(context);

        Assert.True(context.ChangeTracker.Entries().Count() > 0);
    }

    [Fact]
    public void NoTrackingContext_ChangeTrackerEntries_EmptyAfterRead()
    {
        using var db = new TemporaryCatalogDatabase(rowCount: 5);
        using var context = new CatalogContext(db.Path);

        _ = QueryVariants.ReadAllNoTracking(context);

        Assert.Empty(context.ChangeTracker.Entries());
    }
}
