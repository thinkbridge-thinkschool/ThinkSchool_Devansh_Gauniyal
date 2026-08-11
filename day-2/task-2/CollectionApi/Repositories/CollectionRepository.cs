using CollectionApi.Data;
using CollectionApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CollectionApi.Repositories;

public sealed class CollectionRepository(CollectionDbContext dbContext)
    : ICollectionRepository
{
    public Task<Collection?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Collections
            .Include(collection => collection.Items)
            .SingleOrDefaultAsync(collection => collection.Id == id, cancellationToken);

    public async Task AddAsync(Collection collection, CancellationToken cancellationToken)
    {
        await dbContext.Collections.AddAsync(collection, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Collection collection, CancellationToken cancellationToken)
    {
        dbContext.Collections.Update(collection);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Collection collection, CancellationToken cancellationToken)
    {
        dbContext.Collections.Remove(collection);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
