namespace FastApi;

// IDENTICAL shape to day-11/task-1's AuthorQuote (day-3/task-3/QuotesApi/Performance/AuthorQuote.cs).
// Named Quote here since this project is self-contained and has no existing Quote type to
// collide with, unlike task-1's real-API context.
public class Quote
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public int AuthorId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Author? Author { get; set; }
}
