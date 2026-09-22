/**
 * Mirrors QuotesApi.Quotes.QuoteRequests.CreateQuoteRequest
 * (api/QuotesApi/Quotes/QuoteRequests.cs):
 *   public sealed record CreateQuoteRequest(string Text, string? Author = null, string? Description = null);
 * `Author` was added to the real DTO on 2026-08-25 at Devansh's explicit
 * request, as an optional, nullable field with no validation attribute --
 * so it is optional here too, with no validator to match. `Text` still
 * carries no validation attributes at all (grep -n
 * "Required|MaxLength|StringLength|RegularExpression|Range(" across every
 * file in api/QuotesApi returns zero matches for it). `Description` was
 * added the same way: optional, nullable, no validator, so it's left off
 * the request entirely rather than sent as an empty string.
 */
export interface CreateQuoteRequest {
  text: string;
  author?: string;
  description?: string;
}
