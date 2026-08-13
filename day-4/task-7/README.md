# Day 4 — Task 7: Configuration done right

## Status: complete

The real `IOptions` pattern now backs the app's JWT configuration (previously a hand-rolled `GetSection().Get<T>()` + manual eager-validation pattern, functionally similar but not the framework's actual mechanism). All existing tests pass, plus 12 new tests. The precedence chain the task opens with was proven against the real, running app, not asserted. One genuinely surprising, verified (not assumed) finding is documented below: the task's own stated TimeSpan pitfall doesn't match what .NET actually does.

## What changed and where

All changes are in `day-3/task-3/QuotesApi` (approved before starting — see the Step 4 proposal in this conversation):

- `Configuration/InternalJwtOptions.cs` — `AccessTokenLifetimeSeconds` (`int`) → `AccessTokenLifetime` (`TimeSpan`); converted from `sealed class` to `sealed record` to match the task's own example syntax (`public record JwtOptions { ... }`); validation gained an upper-bound check (see the TimeSpan section below).
- `Program.cs` — the manual `AddSingleton(sp => {...})` block for `InternalJwtOptions` replaced with the real `AddOptions<T>().Bind(...).Validate(...).ValidateOnStart()` pattern.
- `Tokens/InternalAccessTokenService.cs`, `Tokens/RefreshTokenService.cs` — constructors now take `IOptions<InternalJwtOptions>` instead of the raw class.
- `appsettings.json` — `"AccessTokenLifetimeSeconds": 900` → `"AccessTokenLifetime": "00:15:00"`.
- `QuotesApi.csproj` — gained a `UserSecretsId` (via `dotnet user-secrets init`).
- Five existing test factories updated for the property rename (mechanical, see "Did this break anything?" below): `day-3/task-3/QuotesApi.Tests/AuthApiFactory.cs`, `day-4/task-2/QuotesApi.Auth.Tests/AuthCoverageApiFactory.cs`, `day-4/task-4/QuotesApi.Logging.Tests/LoggingApiFactory.cs`, `day-4/task-5/QuotesApi.Tracing.Tests/TracingApiFactory.cs`, `day-4/task-6/QuotesApi.Telemetry.Tests/TelemetryIsolationApiFactory.cs`; plus one unit test, `day-4/task-2/QuotesApi.Auth.Tests/ConfigurationValidationTests.cs`.

**Deliberately left unchanged, and why:** `EntraOptions` and `InternalCallerOptions` stay on their existing manual `GetSection().Get<T>()` + `.Validate()` pattern. The task's graded exercise is specifically about the JWT options class; converting the other two isn't necessary for it and would be unrequested churn on Day 3 files.

New: `day-4/task-7/QuotesApi.Options.Tests` (`ProjectReference` only, no Day 3 source duplicated).

## The `JwtOptions` class (really `InternalJwtOptions` — see naming note below)

```csharp
public sealed record InternalJwtOptions
{
    public const string SectionName = "InternalJwt";

    private static readonly TimeSpan MaxAccessTokenLifetime = TimeSpan.FromHours(24);

    public string? Issuer { get; init; }
    public string? Audience { get; init; }
    public string? SigningKeyBase64 { get; init; }
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);

    public byte[] ValidateAndGetSigningKey()
    {
        // ... throws InvalidOperationException for a missing/malformed issuer, audience,
        // signing key, non-positive lifetime, or now also an excessive lifetime -- see
        // "The TimeSpan binding pitfall" below for why that last check exists.
    }
}
```

**Naming note.** The task's example names the class `JwtOptions` and the section `"Jwt"`. This app already has two JWT-bearer schemes — an internal one and Entra ID — so the existing class is (and stays) `InternalJwtOptions`/section `"InternalJwt"`, distinguishing it from `EntraOptions`. Renaming to literally match the task's example would blur that distinction for no benefit. The pattern the task is teaching — a typed options class, bound via `IOptions<T>`, validated at startup — is what changed; the name reflects this app's real shape.

## Binding, validation, and injection

```csharp
// Program.cs
builder.Services
    .AddOptions<InternalJwtOptions>()
    .Bind(builder.Configuration.GetSection(InternalJwtOptions.SectionName))
    .Validate(options =>
    {
        options.ValidateAndGetSigningKey();
        return true;
    })
    .ValidateOnStart();
```

```csharp
// InternalAccessTokenService.cs (a real consumer)
public InternalAccessTokenService(
    IOptions<InternalJwtOptions> options,
    ILogger<InternalAccessTokenService> logger)
{
    _options = options.Value;
    _credentials = new SigningCredentials(
        new SymmetricSecurityKey(_options.ValidateAndGetSigningKey()),
        SecurityAlgorithms.HmacSha256);
    _logger = logger;
}
```

Full appsettings section, DI registration, and injection — all four exercise pieces together — are in `options-setup.md`.

**Why the `Validate` delegate calls `ValidateAndGetSigningKey()` instead of duplicating its rules:** that method already encodes every rule this options class cares about (issuer/audience present, signing key present/valid Base64/long enough, lifetime positive and bounded) and already throws a specific, clear `InvalidOperationException` for whichever rule failed — the exact kind of message the task wants ("a clear message that does NOT echo the secret value"). Re-deriving those rules as a second copy inside `Program.cs` would just create two places that could drift out of sync.

**A detail worth knowing if you extend this: `.Validate()`'s delegate is allowed to throw.** It doesn't have to return `false` — and if it throws, the *original* exception propagates as-is through `IOptions<T>.Value`/`ValidateOnStart()`, not wrapped in a generic `OptionsValidationException`. Confirmed with a standalone probe before relying on it, not assumed: a delegate that throws `InvalidOperationException("custom message")` produces exactly that exception, with that exact message, out of `IHost.StartAsync()`. That's why the tests below assert `InvalidOperationException` with the real message text, not a generic options-validation type.

## `IOptions` vs `IOptionsSnapshot` vs `IOptionsMonitor` — which this app actually needs

**Plain `IOptions<InternalJwtOptions>` — used for both consumers, and here's why the other two don't apply, not just "aren't needed":**

- `InternalAccessTokenService` and `RefreshTokenService` are both registered `AddSingleton`. `IOptionsSnapshot<T>` is **scoped** — injecting it into a singleton isn't merely unnecessary, the DI container refuses it outright (a scope-validation error at startup: a singleton can't consume a scoped service). So `IOptionsSnapshot` is off the table for a structural reason, not a design preference.
- `IOptionsMonitor<T>` would be legal here (it's itself a singleton), and would pick up a live edit to `appsettings.json` while the app is running (the JSON provider reloads on file change by default). But nothing in this app wants that: these two services are deliberately fail-fast singletons — if the signing key changed live, tokens already issued would still validate against the *old* key until rotated everywhere, and the app would need a real key-rotation story (grace period, key ID in the token header, etc.) to use a live-swapped key safely. That's a real feature this app doesn't have, not something to fake with a rebuilt-per-request options wrapper.

So: plain `IOptions<T>`, captured once at first resolution, is the option that actually matches this app's design — not the "default/simplest" choice made without considering the alternatives.

## Configuration precedence — proven against the real, running app

The task's precedence chain: environment variables (highest) → `appsettings.{Environment}.json` → `appsettings.json` (default). Two kinds of proof:

**1. Automated test**, `ConfigurationPrecedenceTests.cs` — proves the underlying mechanism .NET's configuration system relies on (whichever provider is added *last* wins for a shared key), which is exactly how `WebApplication.CreateBuilder` achieves "env vars beat appsettings" (it adds the JSON file first, environment variables after).

**2. Real, captured demonstration** — ran the actual compiled `QuotesApi`, logged in, and decoded the issued access token's `aud` claim (which is set directly from `InternalJwtOptions.Audience`), twice:

```
$ # Run 1 -- no InternalJwt__Audience override, ASPNETCORE_ENVIRONMENT=Development
RUN 1 (no InternalJwt__Audience override) -- aud claim: QuotesApi.InternalClients
RUN 1 -- iss claim: QuotesApi.Internal

$ # Run 2 -- same app, same appsettings.json, only difference: InternalJwt__Audience=env-var-override-proof set
RUN 2 (InternalJwt__Audience=env-var-override-proof set) -- aud claim: env-var-override-proof
RUN 2 -- iss claim: QuotesApi.Internal
```

`QuotesApi.InternalClients` is the real, committed value in `day-3/task-3/QuotesApi/appsettings.json`. Setting the environment variable `InternalJwt__Audience` (the standard ASP.NET Core double-underscore syntax for a nested key `InternalJwt:Audience`) overrode it, while `iss` (untouched, no matching env var set) stayed exactly as configured — proving the override is real precedence, not an accidental full-config replacement.

## The TimeSpan binding pitfall — verified, and it doesn't match the task's own claim

The task states: *"If the value is written as a bare number or a wrong format, binding silently yields `TimeSpan.Zero` and tokens expire immediately."* Checked this directly with a standalone probe against `Microsoft.Extensions.Configuration.Binder` before writing a single test around it, rather than trusting the claim. **It's wrong, in a way worth knowing:**

| Input | What actually happens |
|---|---|
| `"00:15:00"` | Binds correctly to 15 minutes |
| `"900"` (the bare-number mistake) | Binds to **900 DAYS** — `TimeSpan.Parse` treats a bare integer as a day count, not seconds, and not zero |
| `"garbage"` / `""` | **Throws** `InvalidOperationException` immediately during binding — not silent at all |

So the real risk isn't "silent zero, tokens expire instantly" — it's **silent 900-day tokens**, which is arguably worse: a token that outlives any sane rotation policy, issued with no error anywhere. This is also why `AccessTokenLifetime <= TimeSpan.Zero` alone (the check that *would* catch the task's assumed failure mode) isn't sufficient — `InternalJwtOptions.ValidateAndGetSigningKey()` also rejects anything above `MaxAccessTokenLifetime` (24 hours, generous for a legitimate long-lived internal-service token, but instantly catches a units mistake). Both the empirical binding behavior and the new upper-bound guard are covered by real tests in `QuotesApi.Options.Tests/InternalJwtOptionsBindingTests.cs`.

## Secrets: user-secrets locally, Key Vault in production

`QuotesApi.csproj` now has a `UserSecretsId` (`dotnet user-secrets init`). The signing key was **not** generated, chosen, or stored by me — per your instructions, run this yourself:

```
dotnet user-secrets set "InternalJwt:SigningKeyBase64" "<your own base64-encoded value, at least 32 bytes>"
```

(A quick way to generate a valid one locally: `openssl rand -base64 32`.)

**Why `InternalJwt:SigningKeyBase64`, not the task's literal `Jwt:SigningKey`:** this app's existing convention (predating this task) stores the key **Base64-encoded** and validates it decodes to at least 32 bytes — a real, checkable length/entropy guarantee `SecurityAlgorithms.HmacSha256` needs. A raw string under `Jwt:SigningKey` wouldn't carry that guarantee without separate character-length validation, a different (and weaker) check. Kept the existing, already-correct convention rather than degrading it to match the example literally.

**`appsettings.json` has no `SigningKeyBase64` key at all — not even a placeholder.** It already didn't (checked, not assumed — see the Step 2 security finding below), and an absent key produces a clearer, more specific startup error ("Internal JWT signing key is required...") than a placeholder value someone might paste in literally and wonder why authentication silently fails in a new, confusing way.

**Production**: per Task 6, the real secret this app depends on in a genuine cloud deployment (the Application Insights connection string) already comes from Key Vault via `DefaultAzureCredential`, never an environment variable holding the value itself. The same approach applies to the JWT signing key in a real production deployment — a Key Vault reference, resolved by a managed identity, never the raw key value in `appsettings.json`, a committed file, or a log line.

## Did this break anything? Yes — here's exactly what, and why it was safe to fix

Grepped every place `InternalJwt:AccessTokenLifetimeSeconds` was set before writing a single line of the refactor. Five test factories set the old key/format (`day-3/task-3/QuotesApi.Tests/AuthApiFactory.cs` and one per later Day 4 task's factory), plus one unit test that constructs `InternalJwtOptions` directly. All six needed the mechanical rename (`AccessTokenLifetimeSeconds = 0` → `AccessTokenLifetime = TimeSpan.Zero`, `["...AccessTokenLifetimeSeconds"] = "900"` → `["...AccessTokenLifetime"] = "00:15:00"`).

**The switch to `IOptions<T>` constructor injection itself broke nothing** — checked directly: no test anywhere in the repo constructs `InternalAccessTokenService` or `RefreshTokenService` by hand; both are only ever resolved through the DI container in `WebApplicationFactory`-hosted tests, which don't care whether the constructor asks for `InternalJwtOptions` or `IOptions<InternalJwtOptions>`.

Two similarly-named apps were checked and confirmed **out of scope**, not touched: `day-3/task-1`/`day-3/task-2`'s `EntraAuthApi` (a separate app, not referenced by any Day 4 `.slnx`) and `day-2/task-6`/`day-2/task-7`'s `QuotesApi` (an earlier, unrelated codebase with its own, differently-shaped `JwtOptions`).

## C# 14

Confirmed, not assumed, that this project resolves to C# 14 (compiled a throwaway probe and asked MSBuild directly: `LangVersion` → `14.0`, no preview flag, nothing pinned). Looked for a genuine, non-contrived fit for a C# 14 feature (the `field` keyword, extension members) inside this specific refactor and didn't find one — every property here is a plain `init`-only value with no custom accessor logic that would benefit from `field`, and inventing one just to use the keyword would be exactly the kind of contortion this exercise warns against. Converting `InternalJwtOptions` to a `record` (matching the task's own example) is a real, load-bearing change made here, but it's a C# 9 feature, not a C# 14 one.

## Step 2 security finding (from the original inspection)

No real JWT signing key is committed anywhere in this repository. Checked three ways before touching anything: the current `appsettings.json`/`appsettings.Development.json` have no `SigningKeyBase64` key at all (absent, not empty); `git log --all -p` on both files never contains the string "signingkey" in any historical revision; a repository-wide history scan for any `SigningKey`/`ClientSecret` assignment holding a real-looking value found only config *key names* being documented or test-generated values (`Convert.ToBase64String(new byte[32])`, `RandomNumberGenerator.GetBytes(...)`) — never a literal secret. Nothing to rotate.

(Already flagged, unrelated to this task: `Entra:TenantId`/`Entra:ClientId`/`Entra:Audience` in the same `appsettings.json` are real identifiers, not secrets, and not redacted — flagged first in Task 6, out of this task's approved scope.)

## Build, test, and coverage — genuine numbers

```
$ dotnet build day-4/task-7/Task7.slnx
Build succeeded. 0 Warning(s). 0 Error(s).

$ dotnet test day-4/task-7/Task7.slnx
Passed! - Failed: 0, Passed: 1,  Total: 1  - QuotesApi.Telemetry.Tests.dll
Passed! - Failed: 0, Passed: 2,  Total: 2  - QuotesApi.Tracing.Tests.dll
Passed! - Failed: 0, Passed: 2,  Total: 2  - QuotesApi.Logging.Tests.dll
Passed! - Failed: 0, Passed: 12, Total: 12 - QuotesApi.Options.Tests.dll   (new)
Passed! - Failed: 0, Passed: 37, Total: 37 - QuotesApi.Auth.Tests.dll
Passed! - Failed: 0, Passed: 19, Total: 19 - QuotesApi.Tests.dll
```

**73/73 passing** (61 from earlier tasks, unchanged in count and outcome, plus 12 new).

**Coverage**: collected via `dotnet test Task7.slnx --collect:"XPlat Code Coverage"`, six Cobertura reports (one per test project, all measuring the same shared `QuotesApi.dll`). Following the same methodology already established in Task 2's `coverage-summary.txt` — a naive sum across reports double-counts the shared assembly's denominator and understates coverage (it comes out to 58.80% here, which is the wrong number for the reason just given, not a real regression) — the honest figure is the **union** of covered lines across all six reports, via the same `day-4/task-2/scripts/merge_coverage.py`:

```
Reports merged:        6
Union line coverage:   576/610 = 94.43%
```

Well above the Task 1 CI gate's 70% threshold. The uncovered lines are entirely `Program.cs`'s Azure Monitor/Key Vault startup path (lines 62–128) — genuinely never exercised in any test, by design (see Task 6's test-isolation section), not something this task changed.

**On "the Task 1 CI gate"**: `.github/workflows/ci.yml` builds and tests a fixed, hardcoded solution (`day-4/task-1/Task1.slnx`, the standalone `CiDemo` project) — it does not build or test `QuotesApi` or any later task's projects, and nothing here touches `ci.yml` or `Task1.slnx`. CI will show green because this task doesn't affect what it actually runs; the 73/73 and 94.43% numbers above are a genuine, manually-run, separately-reported verification of this task's own code, following the same pattern every Day 4 task after Task 1 has used.
