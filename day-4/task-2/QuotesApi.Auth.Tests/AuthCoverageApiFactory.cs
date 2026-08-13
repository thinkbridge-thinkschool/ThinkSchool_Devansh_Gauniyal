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
