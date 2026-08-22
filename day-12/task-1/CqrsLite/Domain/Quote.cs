namespace CqrsLite.Domain;

// The write side's normalized shape - a bare row plus the foreign key. This entity is what
// SubmitQuoteHandler tracks and saves; it is never returned from the query path, which
// projects straight into its own flat read model instead of materializing this type.
public class Quote
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public int AuthorId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Author? Author { get; set; }
}
