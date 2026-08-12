using System.Security.Cryptography;
using System.Text;
using Quotes.Time;

namespace Quotes.Tokens;

public sealed class RefreshTokenService
{
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly object _gate = new();
    private readonly Dictionary<string, StoredRefreshToken> _tokens = [];
    private readonly IClock _clock;
    private readonly IRefreshTokenGenerator _tokenGenerator;

    public RefreshTokenService(IClock clock, IRefreshTokenGenerator tokenGenerator)
    {
        _clock = clock;
        _tokenGenerator = tokenGenerator;
    }

    public RefreshTokenIssueResult Issue(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new(RefreshTokenIssueStatus.MissingUserId, null);
        }

        lock (_gate)
        {
            var token = CreateToken(Guid.NewGuid(), userId, _clock.UtcNow);
            return new(RefreshTokenIssueStatus.Succeeded, token);
        }
    }

    public RefreshTokenRotationResult Rotate(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return new(RefreshTokenRotationStatus.MissingToken, null);
        }

        lock (_gate)
        {
            var tokenHash = Hash(rawToken);

            if (!_tokens.TryGetValue(tokenHash, out var storedToken))
            {
                return new(RefreshTokenRotationStatus.UnknownToken, null);
            }

            var now = _clock.UtcNow;

            if (storedToken.FamilyRevoked)
            {
                return new(RefreshTokenRotationStatus.FamilyRevoked, null);
            }

            if (storedToken.ExpiresAt <= now)
            {
                return new(RefreshTokenRotationStatus.Expired, null);
            }

            if (storedToken.UsedAt is not null)
            {
                RevokeFamily(storedToken.FamilyId);
                return new(RefreshTokenRotationStatus.ReuseDetected, null);
            }

            storedToken.UsedAt = now;
            var replacement = CreateToken(storedToken.FamilyId, storedToken.UserId, now);

            return new(RefreshTokenRotationStatus.Succeeded, replacement);
        }
    }

    private IssuedRefreshToken CreateToken(Guid familyId, string userId, DateTimeOffset now)
    {
        var rawToken = _tokenGenerator.Generate();
        var tokenHash = Hash(rawToken);
        var expiresAt = now.Add(RefreshTokenLifetime);

        _tokens.Add(
            tokenHash,
            new StoredRefreshToken(familyId, userId, expiresAt));

        return new IssuedRefreshToken(rawToken, now, expiresAt);
    }

    private void RevokeFamily(Guid familyId)
    {
        foreach (var token in _tokens.Values.Where(token => token.FamilyId == familyId))
        {
            token.FamilyRevoked = true;
        }
    }

    private static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private sealed class StoredRefreshToken
    {
        public StoredRefreshToken(Guid familyId, string userId, DateTimeOffset expiresAt)
        {
            FamilyId = familyId;
            UserId = userId;
            ExpiresAt = expiresAt;
        }

        public Guid FamilyId { get; }
        public string UserId { get; }
        public DateTimeOffset ExpiresAt { get; }
        public DateTimeOffset? UsedAt { get; set; }
        public bool FamilyRevoked { get; set; }
    }
}
