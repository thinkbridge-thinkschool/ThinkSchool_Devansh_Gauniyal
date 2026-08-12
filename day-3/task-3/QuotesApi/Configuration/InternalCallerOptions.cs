using System.Security.Cryptography;
using System.Text;

namespace QuotesApi.Configuration;

public sealed class InternalCallerOptions
{
    public const string SectionName = "InternalCaller";

    public string? UserId { get; init; }
    public string? Email { get; init; }
    public string? PasswordSaltBase64 { get; init; }
    public string? PasswordHashBase64 { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(UserId)
            || string.IsNullOrWhiteSpace(Email)
            || string.IsNullOrWhiteSpace(PasswordSaltBase64)
            || string.IsNullOrWhiteSpace(PasswordHashBase64))
        {
            throw new InvalidOperationException(
                "Internal caller ID, email, password salt, and password hash must be configured.");
        }

        try
        {
            _ = Convert.FromBase64String(PasswordSaltBase64);
            _ = Convert.FromBase64String(PasswordHashBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Internal caller password salt and hash must be valid Base64.",
                exception);
        }
    }

    public bool PasswordMatches(string password)
    {
        Validate();
        var salt = Convert.FromBase64String(PasswordSaltBase64!);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations: 100_000,
            HashAlgorithmName.SHA256,
            outputLength: 32);
        var expectedHash = Convert.FromBase64String(PasswordHashBase64!);

        return actualHash.Length == expectedHash.Length
            && CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
