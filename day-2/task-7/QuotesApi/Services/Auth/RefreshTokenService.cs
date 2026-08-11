using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Dtos;
using QuotesApi.Models;
using QuotesApi.Services.Time;

namespace QuotesApi.Services.Auth;

public sealed class RefreshTokenService
{
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly QuotesDbContext _db;
    private readonly JwtTokenService _tokenService;
    private readonly IClock _clock;
    private readonly ILogger<RefreshTokenService> _logger;

    public RefreshTokenService(
        QuotesDbContext db,
        JwtTokenService tokenService,
        IClock clock,
        ILogger<RefreshTokenService> logger)
    {
        _db = db;
        _tokenService = tokenService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<LoginResponse> IssueForLoginAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var response = _tokenService.Issue(user);
        var storedToken = RefreshToken.Create(
            user.Id,
            HashToken(response.RefreshToken),
            _clock.UtcNow.Add(RefreshTokenLifetime));

        _db.RefreshTokens.Add(storedToken);
        await _db.SaveChangesAsync(cancellationToken);

        return response;
    }

    public async Task<LoginResponse?> RotateAsync(
        string rawToken,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(rawToken);
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var storedToken = await _db.RefreshTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(
                token => token.Token == tokenHash,
                cancellationToken);

        if (storedToken is null)
        {
            return null;
        }

        var now = _clock.UtcNow;
        if (storedToken.RevokedAt is not null)
        {
            if (storedToken.ReplacedByToken is not null)
            {
                await RevokeReplacementChainAsync(
                    storedToken,
                    now,
                    cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                _logger.LogWarning(
                    "Refresh-token reuse detected; the replacement chain was revoked for user {UserId}.",
                    storedToken.UserId);
            }

            return null;
        }

        if (storedToken.IsExpired(now))
        {
            return null;
        }

        var response = _tokenService.Issue(storedToken.User);
        var replacementHash = HashToken(response.RefreshToken);
        var replacement = RefreshToken.Create(
            storedToken.UserId,
            replacementHash,
            now.Add(RefreshTokenLifetime));

        storedToken.Rotate(now, replacementHash);
        _db.RefreshTokens.Add(replacement);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            await RevokeAfterConcurrentReuseAsync(tokenHash, cancellationToken);
            return null;
        }
    }

    public async Task RevokeForLogoutAsync(
        string rawToken,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(rawToken);
        var storedToken = await _db.RefreshTokens.SingleOrDefaultAsync(
            token => token.Token == tokenHash,
            cancellationToken);

        if (storedToken is null || storedToken.RevokedAt is not null)
        {
            return;
        }

        storedToken.Revoke(_clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public static string HashToken(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        var bytes = Encoding.UTF8.GetBytes(rawToken);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private async Task RevokeReplacementChainAsync(
        RefreshToken compromisedToken,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var replacementHash = compromisedToken.ReplacedByToken;
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (replacementHash is not null && visited.Add(replacementHash))
        {
            var replacement = await _db.RefreshTokens.SingleOrDefaultAsync(
                token => token.Token == replacementHash,
                cancellationToken);
            if (replacement is null)
            {
                break;
            }

            replacement.Revoke(now);
            replacementHash = replacement.ReplacedByToken;
        }
    }

    private async Task RevokeAfterConcurrentReuseAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var storedToken = await _db.RefreshTokens.SingleOrDefaultAsync(
            token => token.Token == tokenHash,
            cancellationToken);

        if (storedToken?.ReplacedByToken is null)
        {
            return;
        }

        await RevokeReplacementChainAsync(
            storedToken,
            _clock.UtcNow,
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _logger.LogWarning(
            "Concurrent refresh-token reuse detected; the replacement chain was revoked for user {UserId}.",
            storedToken.UserId);
    }
}
