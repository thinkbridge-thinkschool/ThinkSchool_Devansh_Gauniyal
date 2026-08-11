# Day 2, Task 6: Self-Issued JWT Authentication

This self-contained Quotes API extends the completed Task 4 rich-domain baseline with a training-system JWT issuer. `GET` quote routes remain public, while `POST /api/quotes` and `DELETE /api/quotes/{id}` require a valid bearer token.

## Implementation

- `POST /api/auth/login` normalizes email, queries asynchronously with cancellation, and verifies the stored BCrypt hash.
- Valid login returns the explicit snake_case contract `access_token`, `refresh_token`, and `expires_in`.
- Access tokens are signed with HS256 and include `sub`, `email`, `jti`, and `iat` claims.
- Bearer validation checks the HS256 algorithm, signing key, issuer, audience, and lifetime with zero clock skew.
- Refresh-token values are opaque 256-bit random values. Persistence, rotation, revocation, and reuse detection intentionally remain for the later refresh-token task.
- Startup fails when issuer, audience, Base64 signing key, or lifetime is invalid. Decoded key material must contain at least 32 bytes.
- The signing key and development password come from runtime configuration and are not present in committed settings.
- `Users` stores only `Id`, normalized `Email`, and BCrypt `PasswordHash`; a unique index enforces normalized-email uniqueness.
- The Task 4 Quote factory, validation, immutable text, injected clock, cancellation flow, and soft deletion are preserved.

This is an Academy self-issued-token exercise. A public production system should normally delegate authentication to a standards-based OIDC identity provider.

## Safe local setup

From `day-2/task-6`, supply runtime-only values before starting the API:

```bash
export Jwt__SigningKeyBase64="$(openssl rand -base64 32)"
export DevelopmentUser__Email="developer@example.test"
export DevelopmentUser__Password="$(openssl rand -base64 24)"
dotnet run --project QuotesApi/QuotesApi.csproj
```

The non-secret issuer, audience, and 900-second lifetime are in `appsettings.json`. The API applies EF Core migrations and creates the configured development user only when both development-user values are supplied.

## Verification

```bash
dotnet restore Task6.slnx
dotnet build Task6.slnx --no-restore
dotnet test QuotesApi.Auth.Tests/QuotesApi.Auth.Tests.csproj --no-build --no-restore
```

Verified August 11, 2026 with .NET SDK 10.0.302:

- Restore succeeded.
- Build succeeded with 0 warnings and 0 errors.
- Two final authentication-suite runs passed all 16 tests, with reported durations of 3 seconds and 2 seconds.
- Live POST without a token returned 401.
- Live POST with a valid token returned 200.
- Live POST after the correctly signed token expired returned 401 with a Bearer `invalid_token` challenge.
- The live API was stopped and its temporary database and runtime configuration were removed.

See `CURL-EVIDENCE.md` for the sanitized real responses and `MENTOR-NOTES.txt` for the exact paste-ready Academy deliverable.
