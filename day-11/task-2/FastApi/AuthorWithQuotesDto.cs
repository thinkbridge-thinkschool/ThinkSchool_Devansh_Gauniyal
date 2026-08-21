namespace FastApi;

// The projection target. Same field set as task-1's AuthorQuoteSummary
// (day-3/task-3/QuotesApi/Performance/AuthorQuoteSummaryQuery.cs) on purpose: all three
// endpoints below - the reproduced slow one, the projection fix, and the split-query fix -
// return this exact shape, so their outputs can be asserted equal to each other and to
// the logical content of task-1's baseline response.
public sealed record AuthorWithQuotesDto(int AuthorId, string Name, string Country, int QuoteCount);
