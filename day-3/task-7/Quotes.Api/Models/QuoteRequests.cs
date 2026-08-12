namespace Quotes.Api.Models;

public sealed record CreateQuoteRequest(string? Text);

public sealed record UpdateQuoteRequest(string? Text);
