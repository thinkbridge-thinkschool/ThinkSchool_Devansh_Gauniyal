using Quotes.Domain;

namespace Quotes.Validation;

public sealed class CreateQuoteRequestValidator
{
    public const string RequestRequiredError = "Create quote request is required.";

    public ValidationResult Validate(CreateQuoteRequest? request)
    {
        if (request is null)
        {
            return ValidationResult.Invalid(RequestRequiredError);
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.OwnerId))
        {
            errors.Add(Quote.OwnerRequiredError);
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            errors.Add(Quote.TextRequiredError);
        }
        else if (request.Text.Length > Quote.MaximumTextLength)
        {
            errors.Add(Quote.TextTooLongError);
        }

        return errors.Count == 0
            ? ValidationResult.Valid()
            : ValidationResult.Invalid([.. errors]);
    }
}
