namespace QuotesApi.Quotes;

public interface IQuoteRepository
{
    IReadOnlyCollection<Quote> GetAll();
    Quote? Find(int id);
    Quote Create(string ownerId, string text, string? author = null);
    Quote? Update(int id, string text, string? author = null);
    bool Delete(int id);

    // True when some OTHER quote (id != excludingId) already has this same author and
    // text. Used to reject a duplicate on both create and edit -- excludingId lets edit
    // re-save a quote's own unchanged text/author without tripping over itself. Author
    // is required for the comparison to mean anything ("the same author"), so a
    // null/blank author never collides with anything.
    bool ExistsForAuthor(string? author, string text, int? excludingId = null);
}
