namespace DapperComparison;

public sealed record QuoteWallItem
{
    public int QuoteId { get; init; }
    public string QuoteText { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public string AuthorCountry { get; init; } = string.Empty;
    public string SubmittedOn { get; init; } = string.Empty;
}
