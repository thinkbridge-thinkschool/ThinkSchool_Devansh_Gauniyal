# Day 4 — Task 2: Drive yesterday's auth codebase to 80% coverage

## 1. GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-4/task-2/day-4/task-2

## 2. Required mentor notes/deliverables

Baseline (existing 19 tests in `day-3/task-3/QuotesApi.Tests`) was 90.20% line / 66.25% branch. Added 37 new tests in a fresh project (`day-4/task-2/QuotesApi.Auth.Tests`, `ProjectReference` only, no Day 3 source duplicated) targeting every guard clause and edge case the baseline flagged as uncovered, plus removed one dead property. Final: 100.00% line coverage (475/475), merged honestly by line union across both test projects.

## 3. Configuration validation unit tests

```csharp
using QuotesApi.Configuration;

namespace QuotesApi.Auth.Tests;

public class InternalJwtOptionsTests
{
    private const string ValidIssuer = "test-issuer";
    private const string ValidAudience = "test-audience";
    private static readonly string ValidSigningKeyBase64 = Convert.ToBase64String(new byte[32]);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateAndGetSigningKey_MissingIssuer_Throws(string? issuer)
    {
        var options = new InternalJwtOptions
        {
            Issuer = issuer,
            Audience = ValidAudience,
            SigningKeyBase64 = ValidSigningKeyBase64
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetSigningKey());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateAndGetSigningKey_MissingAudience_Throws(string? audience)
    {
        var options = new InternalJwtOptions
        {
            Issuer = ValidIssuer,
            Audience = audience,
            SigningKeyBase64 = ValidSigningKeyBase64
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetSigningKey());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateAndGetSigningKey_MissingSigningKey_Throws(string? signingKeyBase64)
    {
        var options = new InternalJwtOptions
        {
            Issuer = ValidIssuer,
            Audience = ValidAudience,
            SigningKeyBase64 = signingKeyBase64
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetSigningKey());
    }

    [Fact]
    public void ValidateAndGetSigningKey_SigningKeyNotBase64_Throws()
    {
        var options = new InternalJwtOptions
        {
            Issuer = ValidIssuer,
            Audience = ValidAudience,
            SigningKeyBase64 = "not-valid-base64!!!"
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetSigningKey());
    }

    [Fact]
    public void ValidateAndGetSigningKey_SigningKeyTooShort_Throws()
    {
        var options = new InternalJwtOptions
        {
            Issuer = ValidIssuer,
            Audience = ValidAudience,
            SigningKeyBase64 = Convert.ToBase64String(new byte[16])
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetSigningKey());
    }

    [Fact]
    public void ValidateAndGetSigningKey_NonPositiveLifetime_Throws()
    {
        var options = new InternalJwtOptions
        {
            Issuer = ValidIssuer,
            Audience = ValidAudience,
            SigningKeyBase64 = ValidSigningKeyBase64,
            AccessTokenLifetimeSeconds = 0
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetSigningKey());
    }
}

public class EntraOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void ValidateAndGetAuthority_InvalidTenantId_Throws(string? tenantId)
    {
        var options = new EntraOptions
        {
            TenantId = tenantId,
            Audience = "test-audience"
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetAuthority());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateAndGetAuthority_MissingAudience_Throws(string? audience)
    {
        var options = new EntraOptions
        {
            TenantId = Guid.NewGuid().ToString(),
            Audience = audience
        };

        Assert.Throws<InvalidOperationException>(() => options.ValidateAndGetAuthority());
    }
}

public class InternalCallerOptionsTests
{
    private const string ValidUserId = "user-1";
    private const string ValidEmail = "internal.caller@example.test";
    private static readonly string ValidSaltBase64 = Convert.ToBase64String(new byte[16]);
    private static readonly string ValidHashBase64 = Convert.ToBase64String(new byte[32]);

    [Fact]
    public void Validate_MissingUserId_Throws()
    {
        var options = new InternalCallerOptions
        {
            UserId = null,
            Email = ValidEmail,
            PasswordSaltBase64 = ValidSaltBase64,
            PasswordHashBase64 = ValidHashBase64
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_MissingEmail_Throws()
    {
        var options = new InternalCallerOptions
        {
            UserId = ValidUserId,
            Email = null,
            PasswordSaltBase64 = ValidSaltBase64,
            PasswordHashBase64 = ValidHashBase64
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_MissingPasswordSalt_Throws()
    {
        var options = new InternalCallerOptions
        {
            UserId = ValidUserId,
            Email = ValidEmail,
            PasswordSaltBase64 = null,
            PasswordHashBase64 = ValidHashBase64
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_MissingPasswordHash_Throws()
    {
        var options = new InternalCallerOptions
        {
            UserId = ValidUserId,
            Email = ValidEmail,
            PasswordSaltBase64 = ValidSaltBase64,
            PasswordHashBase64 = null
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_SaltNotBase64_Throws()
    {
        var options = new InternalCallerOptions
        {
            UserId = ValidUserId,
            Email = ValidEmail,
            PasswordSaltBase64 = "not-valid-base64!!!",
            PasswordHashBase64 = ValidHashBase64
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_HashNotBase64_Throws()
    {
        var options = new InternalCallerOptions
        {
            UserId = ValidUserId,
            Email = ValidEmail,
            PasswordSaltBase64 = ValidSaltBase64,
            PasswordHashBase64 = "not-valid-base64!!!"
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }
}
```

## 4. Test factory

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace QuotesApi.Auth.Tests;

public sealed class AuthCoverageApiFactory : WebApplicationFactory<Program>
{
    public string Issuer { get; } = "quotes-api.auth-coverage-tests";
    public string Audience { get; } = "quotes-api.auth-coverage-clients";
    public byte[] SigningKey { get; } = RandomNumberGenerator.GetBytes(32);

    public string UserId { get; } = "user-1";
    public string Email { get; } = "internal.caller@example.test";
    public string Password { get; } = "test-password-not-real-" + Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(Password),
                salt,
                iterations: 100_000,
                HashAlgorithmName.SHA256,
                outputLength: 32);

            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InternalJwt:Issuer"] = Issuer,
                ["InternalJwt:Audience"] = Audience,
                ["InternalJwt:SigningKeyBase64"] = Convert.ToBase64String(SigningKey),
                ["InternalJwt:AccessTokenLifetimeSeconds"] = "900",
                // Entra options are resolved eagerly at startup regardless of whether a test
                // exercises the Entra scheme, so a valid (synthetic) value is required here too.
                ["Entra:TenantId"] = Guid.NewGuid().ToString(),
                ["Entra:Audience"] = "quotes-api.auth-coverage-entra-audience",
                ["InternalCaller:UserId"] = UserId,
                ["InternalCaller:Email"] = Email,
                ["InternalCaller:PasswordSaltBase64"] = Convert.ToBase64String(salt),
                ["InternalCaller:PasswordHashBase64"] = Convert.ToBase64String(hash)
            });
        });
    }

    public string CreateToken(
        string? userId = "user-1",
        string? scope = "quotes.write",
        bool includeSubClaim = true,
        DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>();

        if (includeSubClaim && userId is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, userId));
        }

        if (!string.IsNullOrWhiteSpace(scope))
        {
            claims.Add(new Claim("scope", scope));
        }

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now.AddMinutes(-5),
            expires: expires ?? now.AddMinutes(30),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(SigningKey),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

## 5. Integration tests for the remaining gaps

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace QuotesApi.Auth.Tests;

public sealed class AuthCoverageGapTests
{
    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = factory.Email, password = "definitely-the-wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownEmail_ReturnsUnauthorized()
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "not-the-configured-caller@example.test", password = factory.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null, "irrelevant")]
    [InlineData("irrelevant@example.test", null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public async Task Login_MissingCredentials_ReturnsUnauthorized(string? email, string? password)
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetQuotes_Anonymous_ReturnsOkWithAllQuotes()
    {
        // GET /api/quotes currently has no .RequireAuthorization() in Program.cs, unlike every
        // other /api/quotes endpoint. This test documents the actual, current behavior (public
        // read) rather than asserting it is the intended or secure design -- see README.md.
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quotes");
        var quotes = await response.Content.ReadFromJsonAsync<List<QuoteDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(quotes!);
    }

    [Fact]
    public async Task CreateQuote_TokenWithWritePolicyButNoSubjectClaim_ReturnsForbidden()
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();
        var token = factory.CreateToken(includeSubClaim: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes")
        {
            Content = JsonContent.Create(new { text = "New text" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateQuote_UnknownId_ReturnsNotFound()
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();
        var token = factory.CreateToken();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/quotes/999999")
        {
            Content = JsonContent.Create(new { text = "Updated" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_UnknownId_ReturnsNotFound()
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();
        var token = factory.CreateToken();
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/quotes/999999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_TokenWithNoSubjectClaim_ReturnsForbidden()
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();
        var token = factory.CreateToken(includeSubClaim: false);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/quotes/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_EmptyToken_ReturnsUnauthorized()
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refresh_token = "" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WhitespaceToken_ReturnsUnauthorized()
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refresh_token = "   " });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record QuoteDto(int Id, string OwnerId, string Text);
}
```

## 6. Genuine test command

Working directory: `/Users/devansh/thinkschool`

```text
dotnet test day-4/task-2/Task2.slnx --no-build --verbosity normal
```

## 7. Genuine test output

```text
QuotesApi.Auth.Tests.dll: Passed! - Failed: 0, Passed: 37, Skipped: 0, Total: 37, Duration: 1 s
QuotesApi.Tests.dll:      Passed! - Failed: 0, Passed: 19, Skipped: 0, Total: 19, Duration: 2 s

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## 8. Coverage report (cobertura summary)

```
Baseline (existing 19 tests, day-3/task-3/QuotesApi.Tests):
  Overall line-rate:   90.20%  (433/480)
  Overall branch-rate: 66.25%  (53/80)

Final (19 existing + 37 new tests, merged by union of covered lines
across both test projects — not a naive sum, since both instrument
the same QuotesApi.dll):
  Reports merged:        2
  Union line coverage:   475/475 = 100.00%
  Still-uncovered lines after merge: (none)
```

## 9. What did you learn this session?

The most surprising gap wasn't a subtle edge case — `InMemoryQuoteRepository.GetAll()` had zero coverage because `GET /api/quotes` has no `.RequireAuthorization()` at all, unlike every other endpoint on that resource. A missing test and an inconsistent security posture turned out to be the same finding. I also learned that unit tests and integration tests cover genuinely different failure surfaces: five of the largest gaps were config-validation guard clauses (`InternalJwtOptions.ValidateAndGetSigningKey()`) that no HTTP-level test could ever reach, because the test factory always supplies valid config — those needed direct unit tests, not more HTTP requests. Finally, merging coverage across two test projects hitting the same DLL isn't addition — it's a union of covered lines, since summing would double-count the shared denominator.

## 10. What would break this?

`GetQuotes_Anonymous_ReturnsOkWithAllQuotes` locks in the current unauthenticated behavior of `GET /api/quotes` — if that missing auth check is actually a bug rather than intentional, this test breaks the moment it's fixed (which is the point, but it means the assertion is tied to a decision that hasn't been made yet). Branch coverage also can't be proven merged as rigorously as line coverage: this Cobertura output doesn't expose per-line branch attribution, so I can confirm no *line* is dark but not that every individual branch *outcome* is covered across both suites combined. And the "unknown ID" tests (999999) are implicitly coupled to today's seed data (ids 1, 2) — they'd fail safely, not silently, if that ever changes, but they'd need a look.
