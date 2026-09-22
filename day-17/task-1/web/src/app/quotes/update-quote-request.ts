/**
 * Mirrors QuotesApi.Quotes.QuoteRequests.UpdateQuoteRequest
 * (day-17/task-1/api/QuotesApi/Quotes/QuoteRequests.cs):
 *   public sealed record UpdateQuoteRequest(string Text, string? Author = null);
 * Same optional/nullable shape as CreateQuoteRequest.Author -- see
 * create-quote-request.ts.
 */
export interface UpdateQuoteRequest {
  text: string;
  author?: string;
}
