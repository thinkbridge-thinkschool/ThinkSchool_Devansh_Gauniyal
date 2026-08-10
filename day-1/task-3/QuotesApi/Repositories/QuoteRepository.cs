using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public class QuoteRepository : IQuoteRepository
{
    private readonly QuotesDbContext _db;

    public QuoteRepository(QuotesDbContext db)
    {
        _db = db;
    }

   public async Task<List<Quote>> GetAllAsync(
    int page,
    int size,
    CancellationToken cancellationToken)
{
    return await _db.Quotes
        .Skip((page - 1) * size)
        .Take(size)
        .ToListAsync(cancellationToken);
}

    public async Task<Quote?> GetByIdAsync(int id)
    {
        return await _db.Quotes.FindAsync(id);
    }

    public async Task<Quote> CreateAsync(Quote quote)
    {
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();
        return quote;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var quote = await _db.Quotes.FindAsync(id);

        if (quote is null)
        {
            return false;
        }

        _db.Quotes.Remove(quote);
        await _db.SaveChangesAsync();

        return true;
    }
}