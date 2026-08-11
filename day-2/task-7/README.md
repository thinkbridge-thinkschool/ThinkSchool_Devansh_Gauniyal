# Day 2, Task 7: Refresh Tokens with Rotation

## Access tokens and refresh tokens

An access token is a signed bearer credential sent to protected API routes. It expires after exactly 15 minutes, which limits how long a stolen access token remains useful. The API validates its signature, issuer, audience, algorithm, and expiration but does not store it.

A refresh token is a longer-lived random credential used only to obtain a new access/refresh pair. It expires after exactly seven days and is stored server-side so it can be revoked. This Academy implementation returns the raw value only when it is created.

## Why refresh tokens are hashed

The database stores a SHA-256 hash in `RefreshTokens.Token`, never the raw refresh token. A presented raw value is hashed before lookup. If the database is exposed, the stored values cannot be used directly as bearer credentials. `ReplacedByToken` also stores only the replacement token's hash.

Refresh tokens are generated from 32 cryptographically random bytes and Base64Url-encoded. They are not GUIDs, passwords, access tokens, or predictable identifiers.

## Rotation and reuse detection

Every successful refresh is a serializable transaction:

1. Hash and load the presented token.
2. Verify that it exists, is active, and has not expired.
3. Generate a new 15-minute access token and a new seven-day refresh token.
4. Revoke the old row and link it to the new token hash.
5. Insert the replacement and commit both changes atomically.

The old token becomes single-use. If an already-rotated token is presented again, the server treats it as possible leakage. It follows `ReplacedByToken` through the chain, revokes every later token—including the currently active replacement—and logs a warning without logging token material. The client must then authenticate again. This follows the security purpose of refresh-token rotation: replay of an invalidated token reveals compromise, so the active token is revoked.

`POST /api/auth/logout` revokes an active refresh token. It always returns HTTP 204 for unknown, blank, already-revoked, or valid values so it does not reveal whether an arbitrary token exists.

## Database model

`RefreshTokens` contains:

- `Id`
- `Token` (unique SHA-256 hash)
- `UserId` (foreign key to `Users`)
- `ExpiresAt` (`DateTimeOffset`)
- `RevokedAt` (`DateTimeOffset?`)
- `ReplacedByToken` (nullable replacement hash)

The Task 7 migration adds only this table, its user relationship, and the token/user indexes. Existing migrations remain unchanged. `IClock` supplies every application timestamp; integration tests replace it with a fixed clock.

## Run verification

From `day-2/task-7`:

```bash
dotnet restore Task7.slnx
dotnet build Task7.slnx --no-restore
dotnet test QuotesApi.Auth.Tests/QuotesApi.Auth.Tests.csproj --no-build --no-restore
dotnet test QuotesApi.Auth.Tests/QuotesApi.Auth.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~ReusingRotatedRefreshToken_RevokesEntireChain"
```

Actual verification on August 11, 2026 with .NET SDK 10.0.302:

- Build succeeded with 0 warnings and 0 errors.
- The complete suite passed 29/29 tests in 4 seconds.
- `ReusingRotatedRefreshToken_RevokesEntireChain` passed independently twice, each in 1 second.
- All inherited Task 6 authentication behavior remains covered.

## Interview points

- Short-lived access tokens reduce exposure; refresh tokens preserve session continuity.
- A refresh token is a credential and should be stored like a password verifier: keep only a one-way representation server-side.
- Rotation makes tokens single-use and preserves lineage for replay detection.
- The server cannot know whether the legitimate client or an attacker replayed the old token, so revoking the active chain protects both cases.
- Validation, old-token revocation, replacement insertion, and commit must be atomic; concurrency tokens provide an additional lost-update check.
- Refresh-token rotation complements secure transport, client storage, key management, and a production OIDC provider; it does not replace them.
