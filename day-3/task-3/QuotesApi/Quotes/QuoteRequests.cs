namespace QuotesApi.Quotes;

public sealed record CreateQuoteRequest(string Text);
public sealed record UpdateQuoteRequest(string Text);
