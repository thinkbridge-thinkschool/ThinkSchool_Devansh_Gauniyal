using System.Text.Json.Serialization;

namespace QuotesApi.Models;

public sealed class RefreshToken
{
    public const int TokenHashLength = 64;

    public int Id { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public int UserId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByToken { get; private set; }

    [JsonIgnore]
    public User User { get; private set; } = null!;

    private RefreshToken()
    {
    }

    private RefreshToken(int userId, string tokenHash, DateTimeOffset expiresAt)
    {
        UserId = userId;
        Token = tokenHash;
        ExpiresAt = expiresAt;
    }

    public static RefreshToken Create(
        int userId,
        string tokenHash,
        DateTimeOffset expiresAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        if (tokenHash.Length != TokenHashLength)
        {
            throw new ArgumentException(
                $"Refresh-token hash must contain {TokenHashLength} characters.",
                nameof(tokenHash));
        }

        return new RefreshToken(userId, tokenHash, expiresAt);
    }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    public void Rotate(DateTimeOffset now, string replacementTokenHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementTokenHash);
        if (replacementTokenHash.Length != TokenHashLength)
        {
            throw new ArgumentException(
                $"Replacement hash must contain {TokenHashLength} characters.",
                nameof(replacementTokenHash));
        }

        RevokedAt = now;
        ReplacedByToken = replacementTokenHash;
    }

    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
    }
}
