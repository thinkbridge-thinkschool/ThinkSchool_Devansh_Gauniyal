namespace Quotes.Api.Validation;

public static class QuoteRequestValidator
{
    public static Dictionary<string, string[]> Validate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new Dictionary<string, string[]>
            {
                ["text"] = ["Text is required."]
            };
        }

        if (text.Length > 280)
        {
            return new Dictionary<string, string[]>
            {
                ["text"] = ["Text must not exceed 280 characters."]
            };
        }

        return [];
    }
}
