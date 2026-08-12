namespace Quotes.Validation;

public sealed class RefreshTokenRequestValidator
{
    public const string RequestRequiredError = "Refresh token request is required.";
    public const string TokenRequiredError = "Refresh token is required.";

    public ValidationResult Validate(RefreshTokenRequest? request)
    {
        if (request is null)
        {
            return ValidationResult.Invalid(RequestRequiredError);
        }

        return string.IsNullOrWhiteSpace(request.RefreshToken)
            ? ValidationResult.Invalid(TokenRequiredError)
            : ValidationResult.Valid();
    }
}
