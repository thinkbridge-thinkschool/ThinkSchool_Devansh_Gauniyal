using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Configuration;

namespace QuotesApi.Tokens;

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
            expires: now.Add(_options.AccessTokenLifetime).UtcDateTime,
            signingCredentials: _credentials);

        // Never log the token itself -- only the identifiers needed to explain what happened.
        _logger.LogInformation(
            "Access token created for user {UserId} with lifetime {Lifetime}",
            userId,
            _options.AccessTokenLifetime);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
