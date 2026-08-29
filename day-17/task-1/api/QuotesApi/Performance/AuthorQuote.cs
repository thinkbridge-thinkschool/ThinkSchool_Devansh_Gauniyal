namespace QuotesApi.Performance;

// Named AuthorQuote (not Quote) specifically to avoid colliding with the existing
// QuotesApi.Quotes.Quote record used by the auth/CRUD endpoints.
public class AuthorQuote
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public int AuthorId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Author? Author { get; set; }
}
