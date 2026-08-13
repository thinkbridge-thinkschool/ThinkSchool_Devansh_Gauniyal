# Options setup — the four exercise pieces

**Status: complete.** Naming note: this app's class is `InternalJwtOptions` / section `"InternalJwt"`, not the task's literal `JwtOptions`/`"Jwt"` — the app has two JWT-bearer schemes (internal + Entra ID), and the existing name distinguishes them. Full reasoning in `README.md`.

## 1. The options class

```csharp
namespace QuotesApi.Configuration;

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
        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException("Internal JWT issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("Internal JWT audience is required.");
        }

        if (string.IsNullOrWhiteSpace(SigningKeyBase64))
        {
            throw new InvalidOperationException(
                "Internal JWT signing key is required. Configure InternalJwt__SigningKeyBase64.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(SigningKeyBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Internal JWT signing key must be valid Base64.",
                exception);
        }

        if (key.Length < 32)
        {
            throw new InvalidOperationException(
                "Internal JWT signing key must contain at least 32 bytes.");
        }

        if (AccessTokenLifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Internal JWT access-token lifetime must be positive. Check that " +
                "InternalJwt:AccessTokenLifetime is a valid TimeSpan string (e.g. " +
                "\"00:15:00\").");
        }

        if (AccessTokenLifetime > MaxAccessTokenLifetime)
        {
            throw new InvalidOperationException(
                $"Internal JWT access-token lifetime ({AccessTokenLifetime}) exceeds " +
                $"the maximum of {MaxAccessTokenLifetime}. Check that " +
                "InternalJwt:AccessTokenLifetime is in \"hh:mm:ss\" format, not a bare " +
                "number -- a bare number like \"900\" parses as 900 DAYS, not 900 " +
                "seconds, which this check exists specifically to catch.");
        }

        return key;
    }
}
```

## 2. The appsettings section (secret left absent, not a placeholder — see README for why)

```json
{
  "InternalJwt": {
    "Issuer": "QuotesApi.Internal",
    "Audience": "QuotesApi.InternalClients",
    "AccessTokenLifetime": "00:15:00"
  }
}
```

`InternalJwt:SigningKeyBase64` is supplied via `dotnet user-secrets` locally (never committed) — see README's "Secrets" section for the exact command to run yourself.

## 3. The DI registration

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

## 4. Injecting it into a service

```csharp
// Tokens/InternalAccessTokenService.cs
public sealed class InternalAccessTokenService
{
    private readonly InternalJwtOptions _options;
    private readonly SigningCredentials _credentials;
    private readonly ILogger<InternalAccessTokenService> _logger;

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

    public string Create(string userId, string email, DateTimeOffset now)
    {
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: /* ... */ null,
            notBefore: now.UtcDateTime,
            expires: now.Add(_options.AccessTokenLifetime).UtcDateTime,
            signingCredentials: _credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

(`RefreshTokenService` injects `IOptions<InternalJwtOptions>` the same way, for the wire-format `expires_in` seconds value — see README.)
