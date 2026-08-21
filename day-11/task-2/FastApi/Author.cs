namespace FastApi;

// IDENTICAL shape to day-11/task-1's Author (day-3/task-3/QuotesApi/Performance/Author.cs)
// - same properties, same seed volume - so the before/after comparison is like-for-like.
public class Author
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;

    public List<Quote> Quotes { get; set; } = new();
}
