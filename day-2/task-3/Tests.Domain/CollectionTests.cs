using CollectionApi.Exceptions;
using CollectionApi.Models;
using FluentAssertions;
using Xunit;

namespace Tests.Domain;

public sealed class CollectionTests
{
    private static readonly DateTimeOffset FixedAddedAt = new(
        2026, 8, 11, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void CreatingCollection_WithEmptyName_Throws()
    {
        Action act = () => new Collection(string.Empty, ownerId: 1);

        act.Should().Throw<CollectionInvariantException>();
    }

    [Fact]
    public void CreatingCollection_WithNameLongerThan80Characters_Throws()
    {
        Action act = () => new Collection(new string('a', 81), ownerId: 1);

        act.Should().Throw<CollectionInvariantException>();
    }

    [Fact]
    public void AddingFiftyFirstItem_Throws()
    {
        var collection = new Collection("Favorites", ownerId: 1);
        for (var quoteId = 1; quoteId <= Collection.MaximumItems; quoteId++)
        {
            collection.AddItem(quoteId, FixedAddedAt);
        }

        Action act = () => collection.AddItem(51, FixedAddedAt);

        act.Should().Throw<CollectionInvariantException>();
    }

    [Fact]
    public void AddingDuplicateQuoteId_Throws()
    {
        var collection = new Collection("Favorites", ownerId: 1);
        collection.AddItem(quoteId: 42, addedAt: FixedAddedAt);

        Action act = () => collection.AddItem(quoteId: 42, addedAt: FixedAddedAt);

        act.Should().Throw<CollectionInvariantException>();
    }

    [Fact]
    public void RemovingNonExistentItem_Throws()
    {
        var collection = new Collection("Favorites", ownerId: 1);

        Action act = () => collection.RemoveItem(quoteId: 42);

        act.Should().Throw<CollectionInvariantException>();
    }

    [Fact]
    public void AddingThenRemovingItem_LeavesCollectionEmpty()
    {
        var collection = new Collection("Favorites", ownerId: 1);
        collection.AddItem(quoteId: 42, addedAt: FixedAddedAt);

        collection.RemoveItem(quoteId: 42);

        collection.Items.Should().BeEmpty();
    }
}
