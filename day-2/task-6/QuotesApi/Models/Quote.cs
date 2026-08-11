namespace QuotesApi.Models;

public sealed class Quote
{
    public const int MaximumAuthorLength = 200;
    public const int MaximumTextLength = 1000;

    public int Id { get; private set; }
    public string Author { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public bool IsDeleted { get; private set; }

    private Quote()
    {
    }

    private Quote(string author, string text)
    {
        Author = author;
        Text = text;
    }

    public static QuoteCreationResult Create(string? author, string? text)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            return QuoteCreationResult.Failure(new DomainError(
                "quote.author.required",
                "Author is required."));
        }

        if (author.Length > MaximumAuthorLength)
        {
            return QuoteCreationResult.Failure(new DomainError(
                "quote.author.too_long",
                $"Author cannot exceed {MaximumAuthorLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return QuoteCreationResult.Failure(new DomainError(
                "quote.text.required",
                "Text is required."));
        }

        if (text.Length > MaximumTextLength)
        {
            return QuoteCreationResult.Failure(new DomainError(
                "quote.text.too_long",
                $"Text cannot exceed {MaximumTextLength} characters."));
        }

        return QuoteCreationResult.Success(new Quote(author, text));
    }

    public void SoftDelete()
    {
        IsDeleted = true;
    }

    internal void SetCreatedAtUtc(DateTimeOffset createdAtUtc)
    {
        CreatedAtUtc = createdAtUtc;
    }
}
