namespace QuotesApi.Configuration;

public sealed record InternalJwtOptions
{
    public const string SectionName = "InternalJwt";

    // Generous but bounded: catches the verified real TimeSpan-binding pitfall (a bare
    // number like "900" parses as 900 DAYS, not 900 seconds and not TimeSpan.Zero --
    // confirmed empirically, not assumed) while still allowing a legitimately long
    // internal-service token lifetime.
    private static readonly TimeSpan MaxAccessTokenLifetime = TimeSpan.FromHours(24);

    public string? Issuer { get; init; }
    public string? Audience { get; init; }
    public string? SigningKeyBase64 { get; init; }
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);

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

        byte[] key;
        try
        {
            key = Convert.FromBase64String(SigningKeyBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Internal JWT signing key must be valid Base64.",
                exception);
        }

        if (key.Length < 32)
        {
            throw new InvalidOperationException(
                "Internal JWT signing key must contain at least 32 bytes.");
        }

        if (AccessTokenLifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Internal JWT access-token lifetime must be positive. Check that " +
                "InternalJwt:AccessTokenLifetime is a valid TimeSpan string (e.g. " +
                "\"00:15:00\").");
        }

        if (AccessTokenLifetime > MaxAccessTokenLifetime)
        {
            throw new InvalidOperationException(
                $"Internal JWT access-token lifetime ({AccessTokenLifetime}) exceeds " +
                $"the maximum of {MaxAccessTokenLifetime}. Check that " +
                "InternalJwt:AccessTokenLifetime is in \"hh:mm:ss\" format, not a bare " +
                "number -- a bare number like \"900\" parses as 900 DAYS, not 900 " +
                "seconds, which this check exists specifically to catch.");
        }

        return key;
    }
}
