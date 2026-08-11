# Thinkbridge Submission Pack: Day 2, Task 7

## Final GitHub URL

https://github.com/devansh-gauniyal/thinkschool/tree/main/day-2/task-7

## Requirements completed

- Access tokens expire after exactly 900 seconds.
- Refresh tokens expire after exactly seven days.
- Cryptographically random raw refresh tokens are returned only at creation time.
- Only SHA-256 token hashes are stored in `Token` and `ReplacedByToken`.
- Login persists a refresh token and returns the required snake_case token pair.
- `POST /api/auth/refresh` rejects missing, unknown, expired, revoked, and reused tokens.
- Refresh atomically revokes and links the old token while inserting its replacement.
- Reuse detection follows and revokes the complete replacement chain and emits a warning without token material.
- `POST /api/auth/logout` revokes active tokens and always uses a non-revealing HTTP 204 response.
- Task 6 JWT validation and quote authorization behavior remain intact.

## Important files

- `QuotesApi/Models/RefreshToken.cs`: persisted token-hash model and state transitions.
- `QuotesApi/Services/Auth/RefreshTokenService.cs`: issuance, SHA-256 hashing, transactional rotation, reuse detection, chain revocation, and logout.
- `QuotesApi/Program.cs`: login, refresh, and logout endpoint mappings.
- `QuotesApi/Data/QuotesDbContext.cs`: relationship, indexes, and concurrency configuration.
- `QuotesApi/Migrations/20260811082748_AddRefreshTokenRotation.cs`: Task 7 schema change.
- `QuotesApi.Auth.Tests/RefreshTokenTests.cs`: expiry, hashing, rotation, logout, replay, and chain-state integration tests.
- `MENTOR-NOTES.txt`: exact paste-ready Academy evidence.

## Actual build and test results

- Restore: succeeded.
- Build: succeeded with 0 warnings and 0 errors in 0.84 seconds.
- Complete suite: 29 passed, 0 failed, 0 skipped; reported duration 4 seconds.
- Required test: `ReusingRotatedRefreshToken_RevokesEntireChain`.
- Independent required-test runs: 1 passed twice, with 1-second reported duration each.
- Implementation commit: `66444ed96fd7a20befd124b11f260fbfe81d129f`.

No signing key, password, access token, raw refresh token, refresh-token hash, database, `bin`, or `obj` file was committed. Every change is under `day-2/task-7`.
