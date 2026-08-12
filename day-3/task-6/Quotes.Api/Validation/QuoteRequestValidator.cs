namespace Quotes.Api.Validation;

public static class QuoteRequestValidator
{
    public const int MaximumTextLength = 280;

    public static Dictionary<string, string[]> Validate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new()
            {
                ["text"] = ["Quote text is required."]
            };
        }

        if (text.Length > MaximumTextLength)
        {
            return new()
            {
                ["text"] = ["Quote text cannot exceed 280 characters."]
            };
        }

        return [];
    }
}
