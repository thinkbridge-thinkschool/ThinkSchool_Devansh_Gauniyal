namespace CqrsLite.Features.Quotes.Commands;

public enum SubmitQuoteFailureReason
{
    None,
    TextEmpty,
    TextTooLong,
    AuthorNotFound,
    DuplicateQuote
}

// The write side's output shape: an identifier plus a success/failure result - never a
// fully populated entity, and never the read model. A caller who wants the new quote shown
// on the wall re-queries the read path for it.
public sealed record SubmitQuoteResult
{
    public bool Success { get; init; }
    public int? QuoteId { get; init; }
    public SubmitQuoteFailureReason FailureReason { get; init; } = SubmitQuoteFailureReason.None;

    public static SubmitQuoteResult Ok(int quoteId) => new() { Success = true, QuoteId = quoteId };

    public static SubmitQuoteResult Fail(SubmitQuoteFailureReason reason) =>
        new() { Success = false, FailureReason = reason };
}
