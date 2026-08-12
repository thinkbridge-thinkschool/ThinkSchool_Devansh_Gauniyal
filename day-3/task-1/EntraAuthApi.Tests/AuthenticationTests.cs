using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace EntraAuthApi.Tests;

public sealed class AuthenticationTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AuthenticationTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithMalformedJwt_ReturnsUnauthorized()
    {
        var response = await GetProtectedAsync("aaaaa.aaaaa.aaaaa");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidInternalToken_ReturnsOk()
    {
        var token = CreateToken(
            _factory.InternalIssuer,
            _factory.InternalAudience,
            _factory.InternalSigningKey);

        var response = await GetProtectedAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "{\"message\":\"Authentication succeeded.\"}",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidEntraToken_ReturnsOk()
    {
        var token = CreateToken(
            _factory.EntraIssuer,
            _factory.EntraAudience,
            _factory.EntraSigningKey);

        var response = await GetProtectedAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EntraIssuer_WithInternalSignature_IsRejected()
    {
        var token = CreateToken(
            _factory.EntraIssuer,
            _factory.EntraAudience,
            _factory.InternalSigningKey);

        var response = await GetProtectedAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredInternalToken_IsRejected()
    {
        var token = CreateToken(
            _factory.InternalIssuer,
            _factory.InternalAudience,
            _factory.InternalSigningKey,
            expires: DateTime.UtcNow.AddMinutes(-1));

        var response = await GetProtectedAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetProtectedAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private static string CreateToken(
        string issuer,
        string audience,
        byte[] signingKey,
        DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, "test-user")],
            notBefore: now.AddMinutes(-10),
            expires: expires ?? now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(signingKey),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
