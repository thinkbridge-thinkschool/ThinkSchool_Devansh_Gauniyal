using CollectionApi.Models;
using CollectionApi.Repositories;
using CollectionApi.Services.Time;

namespace CollectionApi.Services;

public sealed class CollectionService(
    ICollectionRepository repository,
    IClock clock) : ICollectionService
{
    public async Task<Collection> CreateAsync(
        string? name,
        int ownerId,
        CancellationToken cancellationToken)
    {
        var collection = new Collection(name, ownerId);
        await repository.AddAsync(collection, cancellationToken);
        return collection;
    }

    public async Task<Collection?> AddQuoteAsync(
        int collectionId,
        int quoteId,
        CancellationToken cancellationToken)
    {
        var collection = await repository.GetByIdAsync(collectionId, cancellationToken);
        if (collection is null)
        {
            return null;
        }

        collection.AddItem(quoteId, clock.UtcNow);
        await repository.UpdateAsync(collection, cancellationToken);
        return collection;
    }

    public async Task<bool> RemoveQuoteAsync(
        int collectionId,
        int quoteId,
        CancellationToken cancellationToken)
    {
        var collection = await repository.GetByIdAsync(collectionId, cancellationToken);
        if (collection is null)
        {
            return false;
        }

        collection.RemoveItem(quoteId);
        await repository.UpdateAsync(collection, cancellationToken);
        return true;
    }
}
