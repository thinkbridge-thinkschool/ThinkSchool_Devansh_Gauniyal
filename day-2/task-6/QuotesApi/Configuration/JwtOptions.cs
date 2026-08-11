namespace QuotesApi.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string? Issuer { get; init; }
    public string? Audience { get; init; }
    public string? SigningKeyBase64 { get; init; }
    public int AccessTokenLifetimeSeconds { get; init; } = 900;

    public byte[] ValidateAndGetSigningKey()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException("JWT issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("JWT audience is required.");
        }

        if (string.IsNullOrWhiteSpace(SigningKeyBase64))
        {
            throw new InvalidOperationException("JWT signing key is required.");
        }

        byte[] signingKey;
        try
        {
            signingKey = Convert.FromBase64String(SigningKeyBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "JWT signing key must be valid Base64.",
                exception);
        }

        if (signingKey.Length < 32)
        {
            throw new InvalidOperationException(
                "JWT signing key must contain at least 32 bytes.");
        }

        if (AccessTokenLifetimeSeconds is <= 0 or > 3600)
        {
            throw new InvalidOperationException(
                "JWT access-token lifetime must be between 1 and 3600 seconds.");
        }

        return signingKey;
    }
}
