using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Authentication;

namespace QuotesApi.Tests;

public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public string InternalIssuer { get; } = "QuotesApi.Tests.Internal";
    public string InternalAudience { get; } = "QuotesApi.Tests.InternalClients";
    public byte[] InternalSigningKey { get; } = RandomNumberGenerator.GetBytes(32);

    public string TenantId { get; } = "11111111-1111-1111-1111-111111111111";
    public string EntraAudience { get; } = "22222222-2222-2222-2222-222222222222";
    public byte[] EntraSigningKey { get; } = RandomNumberGenerator.GetBytes(32);

    public string UserId { get; } = "user-1";
    public string Email { get; } = "internal.caller@example.test";
    public string Password { get; } = Guid.NewGuid().ToString("N");

    public string EntraIssuer =>
        $"https://login.microsoftonline.com/{TenantId}/v2.0";

    public string CreateInternalToken(
        string? scope = "quotes.write",
        string? userId = null,
        string? issuer = null,
        string? audience = null,
        DateTime? expires = null) =>
        CreateToken(
            issuer ?? InternalIssuer,
            audience ?? InternalAudience,
            InternalSigningKey,
            userId ?? UserId,
            scope,
            expires);

    public string CreateEntraToken(
        string? scope = "quotes.write",
        string? userId = null) =>
        CreateToken(
            EntraIssuer,
            EntraAudience,
            EntraSigningKey,
            userId ?? UserId,
            scope,
            expires: null);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var passwordSalt = RandomNumberGenerator.GetBytes(16);
            var passwordHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(Password),
                passwordSalt,
                iterations: 100_000,
                HashAlgorithmName.SHA256,
                outputLength: 32);
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InternalJwt:Issuer"] = InternalIssuer,
                ["InternalJwt:Audience"] = InternalAudience,
                ["InternalJwt:SigningKeyBase64"] =
                    Convert.ToBase64String(InternalSigningKey),
                ["InternalJwt:AccessTokenLifetimeSeconds"] = "900",
                ["Entra:TenantId"] = TenantId,
                ["Entra:Audience"] = EntraAudience,
                ["InternalCaller:UserId"] = UserId,
                ["InternalCaller:Email"] = Email,
                ["InternalCaller:PasswordSaltBase64"] =
                    Convert.ToBase64String(passwordSalt),
                ["InternalCaller:PasswordHashBase64"] =
                    Convert.ToBase64String(passwordHash)
            });
        });

        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(
                AuthenticationSchemes.EntraId,
                options =>
                {
                    var configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = EntraIssuer,
                        SigningKeys =
                        {
                            new SymmetricSecurityKey(EntraSigningKey)
                        }
                    };
                    options.Configuration = configuration;
                    options.ConfigurationManager =
                        new StaticConfigurationManager<OpenIdConnectConfiguration>(
                            configuration);
                });
        });
    }

    private static string CreateToken(
        string issuer,
        string audience,
        byte[] signingKey,
        string userId,
        string? scope,
        DateTime? expires)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        if (!string.IsNullOrWhiteSpace(scope))
        {
            claims.Add(new Claim("scope", scope));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now.AddMinutes(-20),
            expires: expires ?? now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(signingKey),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
