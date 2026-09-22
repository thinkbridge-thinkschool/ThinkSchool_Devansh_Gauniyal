namespace QuotesApi.Quotes;

// Author added 2026-08-25 alongside CreateQuoteRequest.Author -- optional,
// nullable, no validation attribute; purely additive to the wire contract.
// Description added the same way: optional, nullable, no validation attribute,
// so it lets the author add free-text context to a quote without becoming a
// required field or breaking any existing caller that never sends it.
public sealed record Quote(int Id, string OwnerId, string Text, string? Author = null, string? Description = null);
