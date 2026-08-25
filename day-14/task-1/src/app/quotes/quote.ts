/**
 * Mirrors the real API's response DTO exactly:
 * day-3/task-3/QuotesApi/Quotes/Quote.cs
 *   public sealed record Quote(int Id, string OwnerId, string Text, string? Author = null);
 * Field names below are camelCase because ASP.NET Core's default Minimal API
 * JSON options use JsonNamingPolicy.CamelCase and Program.cs overrides none of it
 * (confirmed live in Day 13 Task 1). `author` was added 2026-08-25 alongside
 * CreateQuoteRequest.Author -- optional, nullable, present only when the
 * creator supplied one.
 */
export interface Quote {
  id: number;
  ownerId: string;
  text: string;
  author?: string;
}
