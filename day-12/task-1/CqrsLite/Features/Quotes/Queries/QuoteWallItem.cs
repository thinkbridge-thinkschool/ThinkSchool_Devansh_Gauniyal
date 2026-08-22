namespace CqrsLite.Features.Quotes.Queries;

// The read side's output shape: flat, denormalized, projection-shaped for one screen - the
// quote wall. AuthorName and AuthorCountry are folded onto the row here precisely because
// the write model keeps them on a separate Author row; that denormalization is what makes
// this a read model rather than a DTO copy of Quote. No navigation properties, no nested
// Author object, no write-side validation state.
public sealed record QuoteWallItem
{
    public int QuoteId { get; init; }
    public string QuoteText { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public string AuthorCountry { get; init; } = string.Empty;
    public string SubmittedOn { get; init; } = string.Empty;
}
