namespace Quotes.Tokens;

public enum RefreshTokenIssueStatus
{
    Succeeded,
    MissingUserId
}

public enum RefreshTokenRotationStatus
{
    Succeeded,
    MissingToken,
    UnknownToken,
    Expired,
    ReuseDetected,
    FamilyRevoked
}

public sealed record IssuedRefreshToken(
    string Token,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record RefreshTokenIssueResult(
    RefreshTokenIssueStatus Status,
    IssuedRefreshToken? Token);

public sealed record RefreshTokenRotationResult(
    RefreshTokenRotationStatus Status,
    IssuedRefreshToken? Replacement);
