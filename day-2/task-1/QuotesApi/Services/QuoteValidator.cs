using QuotesApi.Models;

namespace QuotesApi.Services;

public sealed class QuoteValidator : IQuoteValidator
{
    public Dictionary<string, string[]> Validate(Quote quote)
    {
        if (!string.IsNullOrWhiteSpace(quote.Author) &&
            !string.IsNullOrWhiteSpace(quote.Text))
        {
            return [];
        }

        return new Dictionary<string, string[]>
        {
            ["quote"] = ["Author and text are required."]
        };
    }
}
