namespace QuotesApi.Quotes;

// Author added 2026-08-25 at Devansh's explicit direction, for Day 14's
// create-a-quote form. Optional (nullable, no validation attribute) so every
// existing caller that only ever sent { text } -- day-4/task-2's
// AuthCoverageGapTests among them -- keeps compiling and keeps working
// unchanged; this is a purely additive change to the wire contract.
public sealed record CreateQuoteRequest(string Text, string? Author = null);
public sealed record UpdateQuoteRequest(string Text);
