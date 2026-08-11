using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Configuration;
using QuotesApi.Dtos;
using QuotesApi.Models;
using QuotesApi.Services.Time;

namespace QuotesApi.Services.Auth;

public sealed class JwtTokenService
{
    private readonly JwtOptions _options;
    private readonly IClock _clock;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(JwtOptions options, IClock clock, byte[] signingKey)
    {
        _options = options;
        _clock = clock;
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(signingKey),
            SecurityAlgorithms.HmacSha256);
    }

    public LoginResponse Issue(User user)
    {
        var issuedAt = _clock.UtcNow;
        var expiresAt = issuedAt.AddSeconds(_options.AccessTokenLifetimeSeconds);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(
                JwtRegisteredClaimNames.Iat,
                issuedAt.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _signingCredentials);

        var refreshToken = Base64UrlEncoder.Encode(
            RandomNumberGenerator.GetBytes(32));

        return new LoginResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            refreshToken,
            _options.AccessTokenLifetimeSeconds);
    }
}
