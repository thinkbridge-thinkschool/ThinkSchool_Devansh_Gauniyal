namespace Quotes.Domain;

public sealed class Quote
{
    public const int MaximumTextLength = 280;
    public const string OwnerRequiredError = "Owner ID is required.";
    public const string TextRequiredError = "Quote text is required.";
    public const string TextTooLongError = "Quote text cannot exceed 280 characters.";

    private Quote(string ownerId, string text)
    {
        OwnerId = ownerId;
        Text = text;
    }

    public string OwnerId { get; }
    public string Text { get; }

    public static QuoteCreationResult Create(string? ownerId, string? text)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return QuoteCreationResult.Failure(OwnerRequiredError);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return QuoteCreationResult.Failure(TextRequiredError);
        }

        if (text.Length > MaximumTextLength)
        {
            return QuoteCreationResult.Failure(TextTooLongError);
        }

        return QuoteCreationResult.Success(new Quote(ownerId, text));
    }
}

public sealed record QuoteCreationResult(bool IsSuccess, Quote? Value, string? Error)
{
    public static QuoteCreationResult Success(Quote quote) => new(true, quote, null);

    public static QuoteCreationResult Failure(string error) => new(false, null, error);
}
