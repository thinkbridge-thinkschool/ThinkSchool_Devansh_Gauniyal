namespace QuotesApi.Quotes;

public sealed class InMemoryQuoteRepository : IQuoteRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Quote> _quotes = new()
    {
        [1] = new(1, "user-1", "Security is a process."),
        [2] = new(2, "user-2", "Policies make intent explicit.")
    };
    private int _nextId = 3;

    public IReadOnlyCollection<Quote> GetAll()
    {
        lock (_gate)
        {
            return _quotes.Values.OrderBy(quote => quote.Id).ToArray();
        }
    }

    public Quote? Find(int id)
    {
        lock (_gate)
        {
            return _quotes.GetValueOrDefault(id);
        }
    }

    public Quote Create(string ownerId, string text, string? author = null)
    {
        lock (_gate)
        {
            var quote = new Quote(_nextId++, ownerId, text, author);
            _quotes.Add(quote.Id, quote);
            return quote;
        }
    }

    public Quote? Update(int id, string text)
    {
        lock (_gate)
        {
            if (!_quotes.TryGetValue(id, out var quote))
            {
                return null;
            }

            var updated = quote with { Text = text };
            _quotes[id] = updated;
            return updated;
        }
    }

    public bool Delete(int id)
    {
        lock (_gate)
        {
            return _quotes.Remove(id);
        }
    }
}
