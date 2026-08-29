namespace QuotesApi.Performance;

// Day 11 Task 1: an additive, self-contained data model for the N+1 / missing-index
// exercise. Deliberately separate from QuotesApi.Quotes.Quote (the existing in-memory,
// ownership-based quote model used by the auth/CRUD endpoints) - this exists purely to
// give the real Week-1 API an authors-with-many-quotes relationship to demonstrate the
// two anti-patterns against, without touching anything already there.
public class Author
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;

    public List<AuthorQuote> Quotes { get; set; } = new();
}
