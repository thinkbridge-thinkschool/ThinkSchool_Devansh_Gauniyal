# Day 4 — Task 2: Drive yesterday's auth codebase to 80% coverage

## Target codebase

`day-3/task-3/QuotesApi` — the "lock down the Quotes API end to end" auth code (JWT bearer
authentication with two schemes, claim- and resource-based authorization, internal access/refresh
token issuance). Of Day 3's three auth-adjacent folders (`day-3/task-1` and `day-3/task-2`'s
scratch `EntraAuthApi`, and `day-3/task-3`'s `QuotesApi`), this is the only one with a combined
`Authentication/` + `Authorization/` + `Tokens/` layer applied to the real, ongoing app — the
later Day 3 tasks (5/6/7) build on `Quotes`/`Quotes.Api` without that auth layer at all.

## Baseline (before any new test was written)

`day-3/task-3/QuotesApi.Tests` already had 19 passing integration tests, but its `.csproj` had no
`coverlet.collector` reference, so `--collect:"XPlat Code Coverage"` produced no coverage file at
all. A single-line `PackageReference` was added there (approved before making the change) purely
so the existing suite's real contribution could be measured — no test logic changed.

```
dotnet test day-3/task-3/Task3.slnx --collect:"XPlat Code Coverage" --results-directory <dir>
Passed! - Failed: 0, Passed: 19, Skipped: 0, Total: 19

Overall line coverage:   90.20%  (433/480)
Overall branch coverage: 66.25%  (53/80)
```

Line coverage already cleared the stated 80% target before writing a single new test — worth
saying plainly. The exercise's real instruction ("for every uncovered branch, add a test or
delete the code") is about branch coverage, which was a genuine 66.25%, so the gaps below were
closed anyway rather than stopping at "80% already met."

## Gaps found and what was done about each

| Gap | File | Why uncovered | Action |
|---|---|---|---|
| `ValidateAndGetSigningKey()` — all 5 guard clauses (missing issuer/audience/key, invalid Base64, key<32 bytes, non-positive lifetime) | `Configuration/InternalJwtOptions.cs` | Test factory always supplies fully valid config | 12 new unit tests, one per failure mode |
| `Validate()` — missing-field guard, invalid-Base64 guard | `Configuration/InternalCallerOptions.cs` | Same | 6 new unit tests |
| `ValidateAndGetAuthority()` — invalid TenantId, missing Audience | `Configuration/EntraOptions.cs` | Same | 6 new unit tests |
| `GetAll()` never called at all | `Quotes/InMemoryQuoteRepository.cs` | `GET /api/quotes` has **no `.RequireAuthorization()`** — unlike every other quote endpoint | New test documenting current (public-read) behavior — see note below |
| Login wrong/missing credentials → 401 | `Program.cs:172-173` | No test ever sends bad credentials to `/api/auth/login` | 6 new tests (wrong password, unknown email, missing/blank email or password) |
| `POST /api/quotes` with a write-scoped token but no `sub` claim → 403 | `Program.cs:199-200` | No test builds a token with scope but no subject | New test |
| `DELETE` of a non-existent quote → 404 | `Program.cs:225-226` | No test deletes an unknown ID | New test |
| `Update()` not-found branch → null → 404 | `Quotes/InMemoryQuoteRepository.cs:44-45` | No test `PUT`s an unknown ID | New test |
| `Rotate()` null/whitespace token guard | `Tokens/RefreshTokenService.cs:40-41` | Existing "unknown token" test uses a non-empty string, missing the blank-token guard | 2 new tests (empty, whitespace) |
| Ownership handler's "no subject claim" branch | `Authorization/OwnQuoteAuthorizationHandler.cs` | Mirrors the `POST` gap above, for `DELETE` | New test |
| `RefreshTokenService.StoredRefreshToken.TokenHash` property | `Tokens/RefreshTokenService.cs:140` | Assigned in the constructor, never read anywhere in the codebase (grepped the whole project to confirm) — a private nested class, never serialized or exposed via any endpoint | **Deleted** (property + constructor parameter), approved before removal |

### The `GET /api/quotes` finding

Every other `/api/quotes` endpoint requires authorization; `GET /api/quotes` does not
(`Program.cs:189-190`). That's either an intentional public-read design or an oversight in the
Day 3 code — not something to silently "fix" as part of a coverage task. Per your direction, a
test (`GetQuotes_Anonymous_ReturnsOkWithAllQuotes`) documents the endpoint's actual current
behavior (anonymous callers get all quotes, including `OwnerId` values) without asserting that
this is secure or intended. No production code changed for this one.

## New test project

`day-4/task-2/QuotesApi.Auth.Tests` — a new xunit project with a single `ProjectReference` to
`day-3/task-3/QuotesApi/QuotesApi.csproj` (no Day 3 source file copied or duplicated). It contains:

- `ConfigurationValidationTests.cs` — 24 pure unit tests directly against `InternalJwtOptions`,
  `InternalCallerOptions`, `EntraOptions` (no HTTP server needed for these).
- `AuthCoverageApiFactory.cs` — a `WebApplicationFactory<Program>` tailored to this project's
  gaps (synthetic signing key, valid-but-fake Entra config since it's resolved eagerly at
  startup regardless of which scheme a test exercises, and a `CreateToken(...)` helper that can
  omit the `sub` claim).
- `AuthCoverageGapTests.cs` — 13 integration tests over real HTTP calls through the factory.

`day-4/task-2/Task2.slnx` includes both the new project and (by reference, not copy)
`day-3/task-3/QuotesApi/QuotesApi.csproj` and `day-3/task-3/QuotesApi.Tests/QuotesApi.Tests.csproj`,
so `dotnet test` can run all 56 tests together and measure the auth code's real combined coverage
in one pass.

## Final result

```
dotnet test day-4/task-2/Task2.slnx --no-build --collect:"XPlat Code Coverage" --results-directory <dir>
QuotesApi.Auth.Tests.dll: Passed! - Failed: 0, Passed: 37, Skipped: 0, Total: 37
QuotesApi.Tests.dll:      Passed! - Failed: 0, Passed: 19, Skipped: 0, Total: 19
```

Both test projects instrument the same `QuotesApi.dll`, so their two coverage reports were
merged by taking the **union of covered lines** (`scripts/merge_coverage.py`) rather than naively
summing `lines-covered`/`lines-valid`, which would double-count the shared denominator:

```
Reports merged:        2
Union line coverage:   475/475 = 100.00%
Still-uncovered lines after merge: (none)
```

**Line coverage: 100.00%, genuinely measured — no lines remain uncovered anywhere in the auth
codebase.** Full numbers, including each suite's standalone contribution and an honest note on
why branch coverage couldn't be rigorously merged across the two reports (Cobertura's XML here
doesn't attribute individual branch outcomes per line), are in `coverage-summary.txt`.

No architecture problem blocked reaching 80% — the existing code was already reasonably testable;
the gaps were genuinely just untested paths (mostly startup-validation guard clauses that a
valid-config-only test factory never exercised), plus the one dead property removed above.

## CI coverage gate (Task 1) — left unchanged

`.github/workflows/ci.yml`'s coverage gate stays at 70%, scoped to `day-4/task-1/Task1.slnx` —
not touched or raised. If that gate were ever pointed at the whole repository (or at this auth
code) instead of its current narrow scope, note for later: aggregating coverage across every
day's practice code would very likely pull the overall percentage back down below both 70% and
80%, for reasons unrelated to any single day's work — the same scope risk flagged in Day 4 Task 1.

## Security notes

No secrets, tokens, or real credentials in any new file. Test signing keys are random bytes
generated per test run (`RandomNumberGenerator.GetBytes(32)`); the test "password" is a random
GUID-suffixed placeholder string, never a real credential.
