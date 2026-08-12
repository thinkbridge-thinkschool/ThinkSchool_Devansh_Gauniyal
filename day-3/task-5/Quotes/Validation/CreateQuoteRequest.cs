namespace Quotes.Validation;

public sealed record CreateQuoteRequest(string? OwnerId, string? Text);
