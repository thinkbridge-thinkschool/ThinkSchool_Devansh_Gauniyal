namespace CqrsLite.Domain;

// The write side's normalized shape - just the columns Author actually owns. Name and
// Country only ever get denormalized onto a read row inside the query projection; they are
// never exposed to callers as part of this type outside the write path.
public class Author
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;

    public List<Quote> Quotes { get; set; } = new();
}
