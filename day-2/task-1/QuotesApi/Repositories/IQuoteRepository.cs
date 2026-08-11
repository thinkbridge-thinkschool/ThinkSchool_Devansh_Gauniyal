using QuotesApi.Models;

namespace QuotesApi.Repositories;

public interface IQuoteRepository
{
    Task<List<Quote>> GetAllAsync(int page, int size, CancellationToken cancellationToken);
    Task<Quote?> GetByIdAsync(int id);
    Task<Quote> CreateAsync(Quote quote);
    Task<bool> DeleteAsync(int id);
}