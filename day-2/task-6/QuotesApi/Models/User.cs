using System.Text.Json.Serialization;

namespace QuotesApi.Models;

public sealed class User
{
    public const int MaximumEmailLength = 320;

    public int Id { get; private set; }
    public string Email { get; private set; } = string.Empty;

    [JsonIgnore]
    public string PasswordHash { get; private set; } = string.Empty;

    private User()
    {
    }

    private User(string email, string passwordHash)
    {
        Email = NormalizeEmail(email);
        PasswordHash = passwordHash;
    }

    public static User Create(string email, string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail.Length > MaximumEmailLength)
        {
            throw new ArgumentException(
                $"Email cannot exceed {MaximumEmailLength} characters.",
                nameof(email));
        }

        return new User(normalizedEmail, passwordHash);
    }

    public static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();
}
