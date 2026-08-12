namespace EntraAuthApi.Configuration;

public sealed class InternalJwtOptions
{
    public const string SectionName = "InternalJwt";

    public string? Issuer { get; init; }
    public string? Audience { get; init; }
    public string? SigningKeyBase64 { get; init; }

    public byte[] ValidateAndGetSigningKey()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException("Internal JWT issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("Internal JWT audience is required.");
        }

        if (string.IsNullOrWhiteSpace(SigningKeyBase64))
        {
            throw new InvalidOperationException(
                "Internal JWT signing key is required. Configure InternalJwt__SigningKeyBase64.");
        }

        byte[] signingKey;
        try
        {
            signingKey = Convert.FromBase64String(SigningKeyBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Internal JWT signing key must be valid Base64.",
                exception);
        }

        if (signingKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Internal JWT signing key must contain at least 32 bytes.");
        }

        return signingKey;
    }
}
