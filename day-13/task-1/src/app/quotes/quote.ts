/**
 * Mirrors QuotesApi.Quotes.Quote (day-3/task-3/QuotesApi/Quotes/Quote.cs):
 *   public sealed record Quote(int Id, string OwnerId, string Text);
 * Field names below are camelCase because ASP.NET Core's default Minimal API
 * JSON options use JsonNamingPolicy.CamelCase and Program.cs overrides none of it.
 */
export interface Quote {
  id: number;
  ownerId: string;
  text: string;
}
