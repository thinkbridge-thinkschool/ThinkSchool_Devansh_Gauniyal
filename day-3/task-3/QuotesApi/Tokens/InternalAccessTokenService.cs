using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Configuration;

namespace QuotesApi.Tokens;

public sealed class InternalAccessTokenService
{
    private readonly InternalJwtOptions _options;
    private readonly SigningCredentials _credentials;

    public InternalAccessTokenService(InternalJwtOptions options)
    {
        _options = options;
        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(options.ValidateAndGetSigningKey()),
            SecurityAlgorithms.HmacSha256);
    }

    public string Create(string userId, string email, DateTimeOffset now)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("scope", "quotes.write"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.AddSeconds(_options.AccessTokenLifetimeSeconds).UtcDateTime,
            signingCredentials: _credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
