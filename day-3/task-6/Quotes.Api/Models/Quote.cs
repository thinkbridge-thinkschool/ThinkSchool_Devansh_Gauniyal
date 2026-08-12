namespace Quotes.Api.Models;

public sealed class Quote
{
    public int Id { get; set; }
    public required string OwnerId { get; set; }
    public required string Text { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
