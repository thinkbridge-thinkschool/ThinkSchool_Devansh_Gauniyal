# Day 4 — Task 7: Configuration done right

## GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-4/task-7/day-4/task-7

## Notes for your mentor

`InternalJwtOptions` now goes through the real `IOptions` pattern (`AddOptions<T>().Bind(...).Validate(...).ValidateOnStart()`) instead of a hand-rolled `GetSection().Get<T>()` + manual eager-validation block. `AccessTokenLifetimeSeconds` (int) became `AccessTokenLifetime` (TimeSpan) per the task, which required fixing five existing test factories and one unit test — all mechanical, all verified safe (no test constructs the two token services directly; they only resolve through DI). Precedence (env var beats appsettings) was proven against the real running app by decoding a real issued token's `aud` claim twice, not asserted. 73/73 tests pass; genuine union coverage is 94.43% (576/610).

One correction worth flagging: the task states a bare-number TimeSpan mistake "silently yields TimeSpan.Zero." Verified directly against `Microsoft.Extensions.Configuration.Binder` before writing any test around it — that's not what happens. A bare number like `"900"` binds to **900 days**, not zero and not a parse failure. Added an upper-bound check (24h max) to `ValidateAndGetSigningKey()` specifically because the real failure mode is a token that lives too long, not one that expires instantly — full detail in `README.md`.

## Exercise: JwtOptions class + appsettings section + DI registration + injection

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
        // throws InvalidOperationException for missing/malformed issuer, audience,
        // signing key, non-positive lifetime, or lifetime > MaxAccessTokenLifetime
        // (see options-setup.md for the full body)
    }
}
```

```json
{
  "InternalJwt": {
    "Issuer": "QuotesApi.Internal",
    "Audience": "QuotesApi.InternalClients",
    "AccessTokenLifetime": "00:15:00"
  }
}
```

```csharp
// Program.cs
builder.Services
    .AddOptions<InternalJwtOptions>()
    .Bind(builder.Configuration.GetSection(InternalJwtOptions.SectionName))
    .Validate(options => { options.ValidateAndGetSigningKey(); return true; })
    .ValidateOnStart();
```

```csharp
// InternalAccessTokenService.cs
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

Full four pieces, unabridged, in `options-setup.md`.

## What did you learn this session?

I learned that an options `.Validate()` delegate can throw its own specific exception instead of just returning false, and that exception propagates unwrapped through `ValidateOnStart()` — confirmed with a standalone probe rather than assumed. I also found the task's stated TimeSpan pitfall doesn't match .NET's real behavior: a bare number binds to that many days, not zero.

## What would break this?

A bare number in `InternalJwt:AccessTokenLifetime` silently produces a multi-day (or multi-century) token lifetime rather than an instantly-expiring one, which my new upper-bound check catches. Renaming the `InternalJwt` section without keeping the `Validate`/`ValidateOnStart` wiring would silently bind an all-default options object with no error at all.
