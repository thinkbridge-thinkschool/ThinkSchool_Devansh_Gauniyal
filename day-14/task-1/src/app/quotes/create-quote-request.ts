/**
 * Mirrors QuotesApi.Quotes.QuoteRequests.CreateQuoteRequest
 * (day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs):
 *   public sealed record CreateQuoteRequest(string Text, string? Author = null);
 * `Author` was added to the real DTO on 2026-08-25 at Devansh's explicit
 * request, as an optional, nullable field with no validation attribute --
 * so it is optional here too, with no validator to match. `Text` still
 * carries no validation attributes at all (grep -n
 * "Required|MaxLength|StringLength|RegularExpression|Range(" across every
 * file in day-3/task-3/QuotesApi returns zero matches for it).
 */
export interface CreateQuoteRequest {
  text: string;
  author?: string;
}
