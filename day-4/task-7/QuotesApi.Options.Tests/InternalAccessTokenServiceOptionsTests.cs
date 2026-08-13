using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuotesApi.Configuration;
using QuotesApi.Tokens;

namespace QuotesApi.Options.Tests;

/// <summary>
/// Proves a real consumer of IOptions&lt;InternalJwtOptions&gt; -- InternalAccessTokenService,
/// as actually wired in Program.cs -- receives the values bound into the options object,
/// not a reimplementation's idea of what it should receive.
/// </summary>
public class InternalAccessTokenServiceOptionsTests
{
    [Fact]
    public void Create_UsesIssuerAudienceAndLifetime_FromInjectedOptions()
    {
        var lifetime = TimeSpan.FromMinutes(42);
        var options = new InternalJwtOptions
        {
            Issuer = "options-test-issuer",
            Audience = "options-test-audience",
            SigningKeyBase64 = Convert.ToBase64String(new byte[32]),
            AccessTokenLifetime = lifetime
        };

        // Fully qualified: this file's own namespace (QuotesApi.Options.Tests) shadows
        // the unqualified "Options" segment that would otherwise resolve to
        // Microsoft.Extensions.Options.Options.
        var service = new InternalAccessTokenService(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<InternalAccessTokenService>.Instance);

        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var token = service.Create("user-42", "user42@example.test", now);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(options.Issuer, jwt.Issuer);
        Assert.Equal(options.Audience, jwt.Audiences.Single());
        Assert.Equal(now.UtcDateTime, jwt.ValidFrom);
        Assert.Equal(now.Add(lifetime).UtcDateTime, jwt.ValidTo);
    }
}
