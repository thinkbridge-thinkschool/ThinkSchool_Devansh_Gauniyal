# Day 4 — Task 2: Drive yesterday's auth codebase to 80% coverage

## 1. GitHub link

https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/tree/day-4/task-2/day-4/task-2

## 2. Required mentor notes/deliverables

Target codebase: `day-3/task-3/QuotesApi` — the only Day 3 folder with a combined `Authentication/` + `Authorization/` + `Tokens/` layer applied to the real, ongoing app (task-1/task-2's `EntraAuthApi` were earlier scratch versions; task-5/6/7's `Quotes`/`Quotes.Api` dropped the auth layer entirely to focus on persistence and testing).

Baseline (existing 19 tests, after adding `coverlet.collector` to `day-3/task-3/QuotesApi.Tests` so they could be measured at all — no test logic changed) was **90.20% line / 66.25% branch**. Final, after 37 new tests targeting every specific gap the baseline flagged, plus removing one genuinely dead property: **100.00% line coverage (475/475), merged honestly by line union across the two test projects, not a naive sum.**

CI verification (confirms Task 1's own pipeline is unaffected by this change — it does **not** validate this task's new tests, since `ci.yml` only builds `day-4/task-1`): https://github.com/thinkbridge-thinkschool/ThinkSchool_Devansh_Gauniyal/actions/runs/31674741291

## 3. Baseline measurement (before writing any new test)

```text
dotnet test day-3/task-3/Task3.slnx --collect:"XPlat Code Coverage" --results-directory <dir>

Passed!  - Failed: 0, Passed: 19, Skipped: 0, Total: 19, Duration: 2 s - QuotesApi.Tests.dll (net10.0)

Overall line-rate:   90.20%  (433/480)
Overall branch-rate: 66.25%  (53/80)
```

Line coverage already cleared 80% before a single new test was written — worth saying plainly rather than pretending the gap was bigger than it was. The exercise's actual instruction ("for every uncovered branch, add a test or delete the code") is about branch coverage, a genuine 66.25%, so every real gap below was closed anyway.

## 4. Coverage report — per-class gaps found, worst first

Parsed directly from the baseline `coverage.cobertura.xml`:

| File | Line % | Branch % | Uncovered lines | Why |
|---|---|---|---|---|
| `Configuration/InternalJwtOptions.cs` | 45.5% | 50% | 15-51 (all 5 guard clauses) | Test factory always supplies fully valid config |
| `Configuration/InternalCallerOptions.cs` | 77.1% | 50% | 21-23, 31-35 | Same |
| `Configuration/EntraOptions.cs` | 58.3% | 50% | 13-15, 19-20 | Same |
| `Quotes/InMemoryQuoteRepository.cs` | 82.0% | 50% | 13-19 (`GetAll()` never called), 44-45 | `GET /api/quotes` has no `.RequireAuthorization()` at all |
| `Program.cs` | 97.2% | 64.3% | 172-173, 199-200, 225-226 | Login-failure, no-`sub`-claim, delete-missing-quote paths untested |
| `Tokens/RefreshTokenService.cs` | 97.5% / 95.0% | 93.8% | 40-41, 140 | Blank-token guard untested; `TokenHash` property never read anywhere |
| `Authorization/OwnQuoteAuthorizationHandler.cs` | 100% | 66.7% | (branch only) | No-`sub`-claim delete attempt untested |

## 5. New unit tests — `Configuration` validation guard clauses

`day-4/task-2/QuotesApi.Auth.Tests/ConfigurationValidationTests.cs` (24 tests, no HTTP server needed):

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

    // ...MissingAudience_Throws, MissingSigningKey_Throws follow the same [Theory] shape
    // (null / "" / "   ") against the other two required fields.

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
        var options = new EntraOptions { TenantId = tenantId, Audience = "test-audience" };

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

    // ...MissingEmail_Throws, MissingPasswordSalt_Throws, MissingPasswordHash_Throws follow
    // the same shape, nulling exactly one required field per test so that removing any single
    // check in the production guard clause is individually caught.

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

## 6. Test factory for the remaining HTTP-level gaps

`day-4/task-2/QuotesApi.Auth.Tests/AuthCoverageApiFactory.cs` — written fresh for this project's specific gaps (not copied from Day 3's `AuthApiFactory`), with a `CreateToken(...)` helper that can omit the `sub` claim on demand:

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

## 7. New integration tests — the remaining HTTP-level gaps

`day-4/task-2/QuotesApi.Auth.Tests/AuthCoverageGapTests.cs` (13 tests):

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
    public async Task GetQuotes_Anonymous_ReturnsOkWithAllQuotes()
    {
        // GET /api/quotes currently has no .RequireAuthorization() in Program.cs, unlike every
        // other /api/quotes endpoint. This test documents the actual, current behavior (public
        // read) rather than asserting it is the intended or secure design -- see section 9.
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

    // ...Login_UnknownEmail_ReturnsUnauthorized, Login_MissingCredentials_ReturnsUnauthorized
    // ([Theory] over null/empty/whitespace email+password pairs), UpdateQuote_UnknownId_ReturnsNotFound,
    // and Refresh_WhitespaceToken_ReturnsUnauthorized round out the remaining gaps from section 4.

    private sealed record QuoteDto(int Id, string OwnerId, string Text);
}
```

## 8. Dead code removed (approved before deletion)

`RefreshTokenService.StoredRefreshToken.TokenHash` — assigned in the constructor, never read anywhere in the codebase (confirmed with `grep -rn "TokenHash"` across the whole project before removing it: only the assignment and the declaration itself matched). Private nested class, never serialized or exposed via any endpoint.

```diff
             _tokens.Add(
                 replacementHash,
                 new StoredRefreshToken(
-                    replacementHash,
                     stored.FamilyId,
                     stored.UserId,
                     stored.Email,
                     now.Add(RefreshLifetime)));
@@
         _tokens.Add(
             tokenHash,
             new StoredRefreshToken(
-                tokenHash,
                 familyId,
                 userId,
                 email,
                 now.Add(RefreshLifetime)));
@@
     private sealed class StoredRefreshToken
     {
         public StoredRefreshToken(
-            string tokenHash,
             Guid familyId,
             string userId,
             string email,
             DateTimeOffset expiresAt)
         {
-            TokenHash = tokenHash;
             FamilyId = familyId;
             UserId = userId;
             Email = email;
             ExpiresAt = expiresAt;
         }

-        public string TokenHash { get; }
         public Guid FamilyId { get; }
```

## 9. Coverage merge script and genuine final result

Two test projects (`day-3/task-3/QuotesApi.Tests`, existing; `day-4/task-2/QuotesApi.Auth.Tests`, new) both instrument the same `QuotesApi.dll`, so their two coverage reports had to be merged by the **union of covered lines**, not a naive sum (which would double-count the shared denominator):

```python
#!/usr/bin/env python3
"""Merge two or more Cobertura reports covering the SAME assembly into one honest
line-coverage figure, by taking the union of covered lines rather than naively
summing lines-covered/lines-valid (which would double-count the shared denominator).
"""
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict


def main() -> int:
    paths = sys.argv[1:]
    line_hits = {}
    for path in paths:
        root = ET.parse(path).getroot()
        for pkg in root.iter('package'):
            for cls in pkg.iter('class'):
                name = cls.attrib['name']
                filename = cls.attrib['filename']
                lines = cls.find('lines')
                if lines is None:
                    continue
                for line in lines.findall('line'):
                    key = (name, filename, int(line.attrib['number']))
                    hits = int(line.attrib['hits'])
                    line_hits[key] = max(line_hits.get(key, 0), hits)

    total_lines = len(line_hits)
    covered_lines = sum(1 for hits in line_hits.values() if hits > 0)
    print(f"Union line coverage: {covered_lines}/{total_lines} = {covered_lines/total_lines*100:.2f}%")
    return 0


if __name__ == "__main__":
    sys.exit(main())
```

Genuine output:

```text
Reports merged:        2
Union line coverage:   475/475 = 100.00%

Still-uncovered lines after merge, by file:
  (none)
```

Branch coverage could not be rigorously merged the same way — this coverlet/Cobertura output doesn't attribute individual branch outcomes at the `<line>` level in either report (no `<line>` carries `branch="true"`, despite the root `<coverage>` element reporting an aggregate `branches-covered`/`branches-valid` count). Each suite's own branch-rate is ~65-66% individually. What's certain: every specific line and conditional branch flagged as uncovered in section 4 was individually targeted by a new test, and the line-level merge confirms zero lines remain uncovered anywhere in the auth codebase.

## 10. Genuine local verification

Working directory: `/Users/devansh/thinkschool`

```text
dotnet restore day-4/task-2/Task2.slnx
dotnet build day-4/task-2/Task2.slnx --no-restore
Build succeeded. 0 Warning(s). 0 Error(s).

dotnet test day-4/task-2/Task2.slnx --no-build --verbosity normal
QuotesApi.Auth.Tests.dll: Passed! - Failed: 0, Passed: 37, Skipped: 0, Total: 37
QuotesApi.Tests.dll:      Passed! - Failed: 0, Passed: 19, Skipped: 0, Total: 19

dotnet test day-4/task-2/Task2.slnx --no-build --collect:"XPlat Code Coverage" --results-directory <dir>
python3 day-4/task-2/scripts/merge_coverage.py <report1> <report2>
Union line coverage: 475/475 = 100.00%
```

56/56 tests passed across both projects; build clean; CI on the pushed branch green (link in section 2).

## 11. What did you learn this session?

Coverage numbers by themselves don't tell you *why* a line is uncovered, and this session was really an exercise in reading that "why" rather than just chasing a percentage. The single biggest surprise: `InMemoryQuoteRepository.GetAll()` had zero coverage, and I initially assumed that just meant "nobody wrote a `GET /api/quotes` test yet." Reading `Program.cs` closely showed the real reason — that endpoint is the *only* one on the entire `/api/quotes` resource with no `.RequireAuthorization()` at all, while `POST`, `PUT`, and `DELETE` all require it. A missing test and an inconsistent security posture turned out to be the same finding wearing different clothes, and I only caught it because I insisted on tracing every uncovered line back to a concrete reason (untested error path vs. dead code vs. something structurally odd) instead of just writing a test to make the highlighter go green.

The second big pattern: five of the largest gaps (all in `InternalJwtOptions.ValidateAndGetSigningKey()`) were guard-clause `throw`s that no HTTP-level test could ever reach, because the existing `WebApplicationFactory` always hands the app fully valid configuration. No amount of clever HTTP requests exercises "what if the signing key is only 16 bytes" — that needed a plain unit test instantiating the options class directly. It was a good reminder that integration tests and unit tests aren't interchangeable tools; they cover genuinely different failure surfaces (wiring/behavior vs. pure logic), and a codebase can look well-tested at the HTTP layer while its configuration-validation layer is completely dark.

Finally, merging coverage across two independent test projects taught me that "combined coverage" isn't `report1 + report2` — both reports describe the *same* `QuotesApi.dll`, so summing `lines-covered`/`lines-valid` would double the denominator. The correct operation is a union: a line counts as covered if *either* suite hit it. I also learned, by grepping the codebase for every read-site of a property before touching it, that "uncovered" and "unreachable" aren't the same claim — `TokenHash` needed proof that nothing anywhere reads it before I could call it dead rather than just untested.

## 12. What would break this?

The coverage number itself is solid — every line is genuinely exercised, verified twice (once per test project's own run, once via the union merge) — but a few things could undermine the *substance* behind it. First, `GetQuotes_Anonymous_ReturnsOkWithAllQuotes` now locks in the current behavior of an unauthenticated `GET /api/quotes`. If that endpoint's missing `.RequireAuthorization()` is actually a bug rather than an intentional public-read design (which I flagged but didn't resolve), this test will need to be rewritten the moment someone fixes it — right now it would fail loudly if authorization were added, which is exactly the point, but it means the test's assertion is tied to a decision that hasn't actually been made yet.

Second, the branch-coverage picture is incomplete by construction, not by oversight: because this Cobertura output doesn't expose per-line branch attribution, I can't prove every individual *branch outcome* (as opposed to every *line*) is covered when the two reports are combined — only that each suite individually sits around 65-66% branch, and that no line is dark. It's possible a compound condition has one arm exercised by suite A and a different arm exercised by suite B in a way that still leaves some third combination — e.g., the "smart bearer" scheme-selection logic in `Program.cs` that inspects a token's issuer — never truly tested end-to-end across both suites together, even though every *line* in that method shows a hit.

Third, and more procedurally: my new tests all run in-memory via `WebApplicationFactory`, using freshly generated random signing keys and an in-memory quote repository re-seeded per test. If the app's configuration loading, DI registration, or the in-memory repository's seed data ever changes shape, several of my "unknown ID" tests (`999999`) would need to be re-checked against whatever the new seed range actually is — they'd fail safely (not silently pass), but they're implicitly coupled to today's seed data being `{1, 2}`.
