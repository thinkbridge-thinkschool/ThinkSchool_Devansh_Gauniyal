/**
 * Mirrors QuotesApi.Quotes.QuoteRequests.CreateQuoteRequest
 * (day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs):
 *   public sealed record CreateQuoteRequest(string Text);
 * This is the DTO's only field, and it carries no validation attributes at
 * all (grep -n "Required|MaxLength|StringLength|RegularExpression|Range("
 * across every file in day-3/task-3/QuotesApi returns zero matches). Do not
 * add a second field here -- an "author" or "title" property would be
 * invented, not real.
 */
export interface CreateQuoteRequest {
  text: string;
}
