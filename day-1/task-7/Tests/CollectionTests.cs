using CollectionApi.Exceptions;
using CollectionApi.Models;

namespace CollectionApi.Tests;

public sealed class CollectionTests
{
    [Fact]
    public void AddItem_WhenQuoteAlreadyExists_ThrowsException()
    {
        var collection = new Collection("Favorites", ownerId: 1);
        collection.AddItem(42);

        var exception = Assert.Throws<CollectionInvariantException>(
            () => collection.AddItem(42));

        Assert.Equal("Quote 42 is already in this collection.", exception.Message);
        Assert.Single(collection.Items);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    public void Constructor_WhenNameIsInvalid_ThrowsException(string name)
    {
        Assert.Throws<CollectionInvariantException>(() => new Collection(name, ownerId: 1));
    }

    [Fact]
    public void Constructor_WhenNameIsLongerThan80Characters_ThrowsException()
    {
        var name = new string('a', 81);

        Assert.Throws<CollectionInvariantException>(() => new Collection(name, ownerId: 1));
    }

    [Fact]
    public void AddItem_WhenCollectionAlreadyHas50Items_ThrowsException()
    {
        var collection = new Collection("Favorites", ownerId: 1);
        for (var quoteId = 1; quoteId <= Collection.MaximumItems; quoteId++)
        {
            collection.AddItem(quoteId);
        }

        var exception = Assert.Throws<CollectionInvariantException>(
            () => collection.AddItem(51));

        Assert.Equal("A collection cannot contain more than 50 items.", exception.Message);
        Assert.Equal(50, collection.Items.Count);
    }

    [Fact]
    public void RemoveItem_WhenQuoteExists_RemovesIt()
    {
        var collection = new Collection("Favorites", ownerId: 1);
        collection.AddItem(42);

        var removed = collection.RemoveItem(42);

        Assert.True(removed);
        Assert.Empty(collection.Items);
    }
}
