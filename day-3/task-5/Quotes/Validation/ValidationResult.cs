namespace Quotes.Validation;

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Valid() => new(true, []);

    public static ValidationResult Invalid(params string[] errors) => new(false, errors);
}
