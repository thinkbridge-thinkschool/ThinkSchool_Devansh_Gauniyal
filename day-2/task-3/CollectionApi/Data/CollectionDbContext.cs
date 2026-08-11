using CollectionApi.Models;
using CollectionApi.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace CollectionApi.Data;

public sealed class CollectionDbContext(DbContextOptions<CollectionDbContext> options)
    : DbContext(options)
{
    public DbSet<Collection> Collections => Set<Collection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var collection = modelBuilder.Entity<Collection>();
        collection.HasKey(value => value.Id);
        collection.Property(value => value.Name).HasMaxLength(80).IsRequired();

        collection.OwnsMany(value => value.Items, items =>
        {
            items.ToTable("CollectionItems");
            items.WithOwner().HasForeignKey("CollectionId");
            items.HasKey("CollectionId", nameof(CollectionItem.QuoteId));
            items.Property(value => value.QuoteId)
                .ValueGeneratedNever()
                .IsRequired();
            items.Property(value => value.AddedAt).IsRequired();
        });

        collection.Navigation(value => value.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
