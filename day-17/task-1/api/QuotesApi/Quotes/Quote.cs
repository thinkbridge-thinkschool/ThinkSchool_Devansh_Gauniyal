namespace QuotesApi.Quotes;

// Author added 2026-08-25 alongside CreateQuoteRequest.Author -- optional,
// nullable, no validation attribute; purely additive to the wire contract.
public sealed record Quote(int Id, string OwnerId, string Text, string? Author = null);
