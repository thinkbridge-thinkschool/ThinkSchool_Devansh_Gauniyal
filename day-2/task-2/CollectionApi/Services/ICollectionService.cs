using CollectionApi.Models;

namespace CollectionApi.Services;

public interface ICollectionService
{
    Task<Collection> CreateAsync(
        string? name,
        int ownerId,
        CancellationToken cancellationToken);

    Task<Collection?> AddQuoteAsync(
        int collectionId,
        int quoteId,
        CancellationToken cancellationToken);

    Task<bool> RemoveQuoteAsync(
        int collectionId,
        int quoteId,
        CancellationToken cancellationToken);
}
