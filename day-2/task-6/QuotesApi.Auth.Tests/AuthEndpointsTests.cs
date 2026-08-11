using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;

namespace QuotesApi.Auth.Tests;

public sealed class AuthEndpointsTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsExactTokenResponse()
    {
        var response = await LoginAsync(_factory.Email, _factory.Password);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("access_token", "refresh_token", "expires_in");
        json.RootElement.GetProperty("access_token").GetString().Should().NotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("refresh_token").GetString().Should().NotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("expires_in").GetInt32().Should().Be(900);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var response = await LoginAsync(_factory.Email, $"{_factory.Password}-wrong");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithUnknownUser_ReturnsUnauthorized()
    {
        var response = await LoginAsync("unknown.user@example.test", _factory.Password);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Root_WithoutToken_RemainsPublic()
    {
        var response = await _client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetQuotes_WithoutToken_RemainsPublic()
    {
        var response = await _client.GetAsync("/api/quotes?page=1&size=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostQuote_WithoutToken_ReturnsUnauthorized()
    {
        var response = await PostQuoteAsync(token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostQuote_WithValidToken_ReturnsOk()
    {
        var token = await GetAccessTokenAsync();
        var response = await PostQuoteAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostQuote_WithExpiredToken_ReturnsUnauthorizedWithBearerChallenge()
    {
        var token = CreateExpiredToken();
        var response = await PostQuoteAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().ContainSingle();
        response.Headers.WwwAuthenticate.Single().Scheme.Should().Be("Bearer");
        response.Headers.WwwAuthenticate.Single().Parameter.Should().Contain("invalid_token");
    }

    [Fact]
    public async Task DeleteQuote_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.DeleteAsync("/api/quotes/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AccessToken_ContainsExpectedIssuerAudienceAndSubject()
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(
            await GetAccessTokenAsync());

        token.Issuer.Should().Be(_factory.Issuer);
        token.Audiences.Should().ContainSingle(_factory.Audience);
        token.Subject.Should().NotBeNullOrWhiteSpace();
        token.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Email)
            .Value.Should().Be(_factory.Email);
    }

    [Fact]
    public async Task AccessToken_ContainsJtiIatAndFifteenMinuteExpiry()
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(
            await GetAccessTokenAsync());
        var issuedAt = long.Parse(token.Claims.Single(
            claim => claim.Type == JwtRegisteredClaimNames.Iat).Value);

        token.Id.Should().NotBeNullOrWhiteSpace();
        token.Payload.Expiration.Should().NotBeNull();
        (token.Payload.Expiration!.Value - issuedAt).Should().Be(900);
    }

    [Fact]
    public async Task LoginResponse_DoesNotExposePasswordHash()
    {
        var response = await LoginAsync(_factory.Email, _factory.Password);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContainEquivalentOf("passwordHash");
        body.Should().NotContainEquivalentOf("password_hash");
        body.Should().NotContain("$2");
    }

    [Fact]
    public async Task RefreshToken_IsOpaqueAndUniqueForEachLogin()
    {
        var first = await GetRefreshTokenAsync();
        var second = await GetRefreshTokenAsync();

        first.Should().NotBe(second);
        first.Split('.').Should().ContainSingle();
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private async Task<string> GetAccessTokenAsync()
    {
        var response = await LoginAsync(_factory.Email, _factory.Password);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<string> GetRefreshTokenAsync()
    {
        var response = await LoginAsync(_factory.Email, _factory.Password);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("refresh_token").GetString()!;
    }

    private async Task<HttpResponseMessage> PostQuoteAsync(string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes")
        {
            Content = JsonContent.Create(new
            {
                author = "Authentication integration test",
                text = $"Protected quote {Guid.NewGuid():N}"
            })
        };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request);
    }

    private string CreateExpiredToken()
    {
        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(_factory.SigningKey),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _factory.Issuer,
            audience: _factory.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, "1"),
                new Claim(JwtRegisteredClaimNames.Email, _factory.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(
                    JwtRegisteredClaimNames.Iat,
                    new DateTimeOffset(now.AddMinutes(-20)).ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64)
            ],
            notBefore: now.AddMinutes(-20),
            expires: now.AddMinutes(-5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
