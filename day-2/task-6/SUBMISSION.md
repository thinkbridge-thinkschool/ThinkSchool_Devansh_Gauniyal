# Thinkbridge Submission Pack: Day 2, Task 6

## GitHub link

https://github.com/devansh-gauniyal/thinkschool/tree/main/day-2/task-6

## Mentor notes

Paste the complete contents of `MENTOR-NOTES.txt`. It contains the complete login endpoint, response DTO, all three required real curl scenarios and sanitized responses, the expired-token Bearer challenge, verification results, and the implementation commit hash.

## Implementation summary

- Added the `Users` table with a unique normalized email and BCrypt password hashes.
- Added fail-fast strongly typed JWT configuration sourced through `IConfiguration`.
- Issued HS256 access tokens with a runtime-only 256-bit-or-larger key and `sub`, `email`, `jti`, and `iat` claims.
- Validated signature, issuer, audience, lifetime, signing key, and HS256 algorithm with zero clock skew.
- Returned explicit snake_case login fields and a cryptographically random opaque refresh token.
- Kept quote reads and login public while protecting quote creation and deletion.
- Preserved the Task 4 rich Quote invariants and returned the Academy-required HTTP 200 for authenticated creation.
- Added 16 integration and configuration tests using a migrated isolated SQLite database and runtime-generated test credentials/key.

## Actual verification

- Restore: succeeded.
- Build: succeeded with 0 warnings and 0 errors.
- Authentication tests: 16 passed, 0 failed, 0 skipped in each of two final runs; reported durations were 3 seconds and 2 seconds.
- No-token POST: HTTP 401 with Bearer challenge.
- Valid-token POST: HTTP 200 with persisted Quote response.
- Expired-token POST: HTTP 401 with `WWW-Authenticate: Bearer error="invalid_token"`.
- GET quotes: public and HTTP 200 in integration testing.
- DELETE without a token: HTTP 401 in integration testing.
- API shutdown and temporary-file cleanup: completed.
- Implementation commit: `d22304f663fa810c0baf4b4cbbd6058906f8f5a4`.
- Scope: only `day-2/task-6` changed.
