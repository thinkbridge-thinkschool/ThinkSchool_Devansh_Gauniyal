namespace DapperComparison;

public class Quote
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public int AuthorId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Author? Author { get; set; }
}
