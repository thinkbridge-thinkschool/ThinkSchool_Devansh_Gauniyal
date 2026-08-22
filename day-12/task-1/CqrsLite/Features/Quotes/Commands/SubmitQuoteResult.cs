namespace CqrsLite.Features.Quotes.Commands;

public enum SubmitQuoteFailureReason
{
    None,
    TextEmpty,
    TextTooLong,
    AuthorNotFound,
    DuplicateQuote
}

public sealed record SubmitQuoteResult
{
    public bool Success { get; init; }
    public int? QuoteId { get; init; }
    public SubmitQuoteFailureReason FailureReason { get; init; } = SubmitQuoteFailureReason.None;

    public static SubmitQuoteResult Ok(int quoteId) => new() { Success = true, QuoteId = quoteId };

    public static SubmitQuoteResult Fail(SubmitQuoteFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}
