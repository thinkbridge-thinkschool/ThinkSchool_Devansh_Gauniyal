namespace QuotesApi.Dtos;

public sealed record CreateQuoteRequest(string? Author, string? Text);
