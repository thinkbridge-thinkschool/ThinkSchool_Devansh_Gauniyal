namespace CqrsLite.Features.Quotes.Queries;

// The read side's input shape. The quote wall shows everything, newest first, so there is
// nothing to parameterize yet - this exists as its own type so a future filter (an author,
// a date range) has somewhere to go without touching the handler's signature or the command
// path at all.
public sealed record QuoteWallQuery;
