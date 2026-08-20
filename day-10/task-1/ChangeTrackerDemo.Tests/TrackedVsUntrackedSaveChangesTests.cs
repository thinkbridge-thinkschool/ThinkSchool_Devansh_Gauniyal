using ChangeTrackerDemo;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ChangeTrackerDemo.Tests;

// Demonstration (b): a tracked entity's modification survives SaveChanges(); an
// AsNoTracking() entity's modification is silently dropped, because there is no
// snapshot for SaveChanges() to diff the in-memory instance against. Both are proven
// by re-reading from a FRESH DbContext afterwards, not by trusting the same context.
public class TrackedVsUntrackedSaveChangesTests
{
    [Fact]
    public void TrackedEntity_ModifiedAndSaved_PersistsChange()
    {
        using var db = new TemporaryCatalogDatabase(rowCount: 5);
        string updatedName;

        using (var context = new CatalogContext(db.Path))
        {
            var product = context.Products.Single(p => p.Id == 1);
            Assert.True(context.ChangeTracker.Entries().Count() > 0);

            updatedName = product.Name + "-Updated";
            product.Name = updatedName;
            context.SaveChanges();
        }

        using (var freshContext = new CatalogContext(db.Path))
        {
            var reread = freshContext.Products.Single(p => p.Id == 1);
            Assert.Equal(updatedName, reread.Name);
        }
    }

    [Fact]
    public void NoTrackingEntity_ModifiedAndSaved_DoesNotPersist()
    {
        using var db = new TemporaryCatalogDatabase(rowCount: 5);

        string originalName;
        using (var readContext = new CatalogContext(db.Path))
        {
            originalName = readContext.Products.AsNoTracking().Single(p => p.Id == 1).Name;
        }

        using (var context = new CatalogContext(db.Path))
        {
            var product = context.Products.AsNoTracking().Single(p => p.Id == 1);
            Assert.Empty(context.ChangeTracker.Entries());

            product.Name = originalName + "-ShouldNotPersist";
            context.SaveChanges(); // no-op: nothing tracked, so nothing to diff or write
        }

        using (var freshContext = new CatalogContext(db.Path))
        {
            var reread = freshContext.Products.Single(p => p.Id == 1);
            Assert.Equal(originalName, reread.Name);
        }
    }
}
