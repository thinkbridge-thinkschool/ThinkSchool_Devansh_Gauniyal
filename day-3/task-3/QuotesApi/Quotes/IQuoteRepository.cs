namespace QuotesApi.Quotes;

public interface IQuoteRepository
{
    IReadOnlyCollection<Quote> GetAll();
    Quote? Find(int id);
    Quote Create(string ownerId, string text, string? author = null);
    Quote? Update(int id, string text);
    bool Delete(int id);
}
