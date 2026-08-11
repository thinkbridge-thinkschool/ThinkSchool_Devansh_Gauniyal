using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Services.Time;

namespace QuotesApi.Repositories;

public class QuoteRepository : IQuoteRepository
{
    private readonly QuotesDbContext _db;
    private readonly IClock _clock;

    public QuoteRepository(QuotesDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<List<Quote>> GetAllAsync(
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        return await _db.Quotes
            .OrderBy(quote => quote.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
    }

    public async Task<Quote?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await _db.Quotes.SingleOrDefaultAsync(
            quote => quote.Id == id,
            cancellationToken);
    }

    public async Task<Quote> CreateAsync(
        Quote quote,
        CancellationToken cancellationToken)
    {
        quote.SetCreatedAtUtc(_clock.UtcNow);
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync(cancellationToken);
        return quote;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var quote = await GetByIdAsync(id, cancellationToken);

        if (quote is null)
        {
            return false;
        }

        quote.SoftDelete();
        await _db.SaveChangesAsync(cancellationToken);

        return true;
    }
}
