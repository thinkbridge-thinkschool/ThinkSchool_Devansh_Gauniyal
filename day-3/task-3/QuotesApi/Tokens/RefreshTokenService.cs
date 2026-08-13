using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using QuotesApi.Configuration;

namespace QuotesApi.Tokens;

public sealed class RefreshTokenService
{
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(7);

    private readonly object _gate = new();
    private readonly Dictionary<string, StoredRefreshToken> _tokens = [];
    private readonly InternalAccessTokenService _accessTokens;
    private readonly InternalJwtOptions _jwtOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RefreshTokenService> _logger;

    public RefreshTokenService(
        InternalAccessTokenService accessTokens,
        InternalJwtOptions jwtOptions,
        TimeProvider timeProvider,
        ILogger<RefreshTokenService> logger)
    {
        _accessTokens = accessTokens;
        _jwtOptions = jwtOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public TokenPair Issue(string userId, string email)
    {
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            var familyId = Guid.NewGuid();
            var pair = CreatePair(userId, email, familyId, now);
            _logger.LogInformation(
                "Refresh token issued for user {UserId} in family {FamilyId}",
                userId,
                familyId);
            return pair;
        }
    }

    public TokenPair? Rotate(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        // Custom span: rotation involves a hash lookup, expiry/revocation checks, and
        // reuse-family revocation -- genuine branching logic that automatic AspNetCore/
        // HttpClient instrumentation never sees, since it's all in-process work with no
        // HTTP call or EF query of its own.
        using var activity = Telemetry.Source.StartActivity("refresh-token.rotate");

        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            var tokenHash = Hash(rawToken);

            if (!_tokens.TryGetValue(tokenHash, out var stored)
                || stored.ExpiresAt <= now)
            {
                activity?.SetTag("refresh_token.outcome", "not_found_or_expired");
                _logger.LogWarning("Refresh token rejected: unknown or expired");
                return null;
            }

            // user.id is PII -- see README for the retention note that applies to it here
            // too, same as the rest of the auth flow's logging.
            activity?.SetTag("user.id", stored.UserId);

            if (stored.RevokedAt is not null)
            {
                if (stored.ReplacedByHash is not null)
                {
                    // A previously-rotated token was presented again -- a strong signal the
                    // raw token leaked to a second party. Revoking the whole family (every
                    // token descended from the original Issue) invalidates the attacker's
                    // copy along with the legitimate holder's, forcing a fresh login.
                    activity?.SetTag("refresh_token.outcome", "reuse_detected");
                    _logger.LogWarning(
                        "Refresh token reuse detected; revoking family {FamilyId}",
                        stored.FamilyId);
                    RevokeFamily(stored.FamilyId, now);
                }
                else
                {
                    activity?.SetTag("refresh_token.outcome", "already_revoked");
                }

                return null;
            }

            var replacementRaw = CreateRawToken();
            var replacementHash = Hash(replacementRaw);
            stored.RevokedAt = now;
            stored.ReplacedByHash = replacementHash;

            _tokens.Add(
                replacementHash,
                new StoredRefreshToken(
                    stored.FamilyId,
                    stored.UserId,
                    stored.Email,
                    now.Add(RefreshLifetime)));

            activity?.SetTag("refresh_token.outcome", "rotated");
            _logger.LogInformation(
                "Refresh token rotated for user {UserId} in family {FamilyId}",
                stored.UserId,
                stored.FamilyId);

            return new TokenPair(
                _accessTokens.Create(stored.UserId, stored.Email, now),
                replacementRaw,
                _jwtOptions.AccessTokenLifetimeSeconds);
        }
    }

    private TokenPair CreatePair(
        string userId,
        string email,
        Guid familyId,
        DateTimeOffset now)
    {
        var rawToken = CreateRawToken();
        var tokenHash = Hash(rawToken);
        _tokens.Add(
            tokenHash,
            new StoredRefreshToken(
                familyId,
                userId,
                email,
                now.Add(RefreshLifetime)));

        return new TokenPair(
            _accessTokens.Create(userId, email, now),
            rawToken,
            _jwtOptions.AccessTokenLifetimeSeconds);
    }

    private void RevokeFamily(Guid familyId, DateTimeOffset now)
    {
        foreach (var token in _tokens.Values.Where(value => value.FamilyId == familyId))
        {
            token.RevokedAt ??= now;
        }
    }

    private static string CreateRawToken() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    internal static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(rawToken)));

    private sealed class StoredRefreshToken
    {
        public StoredRefreshToken(
            Guid familyId,
            string userId,
            string email,
            DateTimeOffset expiresAt)
        {
            FamilyId = familyId;
            UserId = userId;
            Email = email;
            ExpiresAt = expiresAt;
        }

        public Guid FamilyId { get; }
        public string UserId { get; }
        public string Email { get; }
        public DateTimeOffset ExpiresAt { get; }
        public DateTimeOffset? RevokedAt { get; set; }
        public string? ReplacedByHash { get; set; }
    }
}
