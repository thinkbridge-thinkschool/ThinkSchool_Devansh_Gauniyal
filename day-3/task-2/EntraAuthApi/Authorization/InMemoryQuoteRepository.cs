namespace EntraAuthApi.Authorization;

public sealed class InMemoryQuoteRepository : IQuoteRepository
{
    private static readonly IReadOnlyDictionary<int, QuoteResource> Quotes =
        new Dictionary<int, QuoteResource>
        {
            [1] = new(1, "user-1"),
            [2] = new(2, "user-2")
        };

    public QuoteResource? Find(int id) =>
        Quotes.GetValueOrDefault(id);
}
