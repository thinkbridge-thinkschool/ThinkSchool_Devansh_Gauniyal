namespace QuotesApi.Models;

public sealed class QuoteCreationResult
{
    private QuoteCreationResult(Quote? value, DomainError? error)
    {
        Value = value;
        Error = error;
    }

    public Quote? Value { get; }
    public DomainError? Error { get; }
    public bool IsSuccess => Value is not null;

    internal static QuoteCreationResult Success(Quote quote) =>
        new(quote, error: null);

    internal static QuoteCreationResult Failure(DomainError error) =>
        new(value: null, error);
}
