using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Services.Auth;

namespace QuotesApi.Auth.Tests;

public sealed class RefreshTokenTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public RefreshTokenTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_StoresOnlyRefreshTokenHash()
    {
        var pair = await LoginAsync();
        var tokenHash = RefreshTokenService.HashToken(pair.RefreshToken);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        var stored = await db.RefreshTokens.SingleAsync(
            token => token.Token == tokenHash);

        (stored.Token == pair.RefreshToken).Should().BeFalse();
        (stored.Token == tokenHash).Should().BeTrue();
        stored.Token.Should().HaveLength(RefreshToken.TokenHashLength);
    }

    [Fact]
    public async Task Refresh_RotatesTokenSuccessfully()
    {
        var firstPair = await LoginAsync();
        var response = await RefreshAsync(firstPair.RefreshToken);
        var secondPair = await ReadPairAsync(response);
        var firstHash = RefreshTokenService.HashToken(firstPair.RefreshToken);
        var secondHash = RefreshTokenService.HashToken(secondPair.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (firstPair.RefreshToken == secondPair.RefreshToken).Should().BeFalse();
        secondPair.ExpiresIn.Should().Be(900);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        var firstStored = await db.RefreshTokens.SingleAsync(
            token => token.Token == firstHash);
        var secondStored = await db.RefreshTokens.SingleAsync(
            token => token.Token == secondHash);

        firstStored.RevokedAt.Should().Be(_factory.Clock.UtcNow);
        (firstStored.ReplacedByToken == secondHash).Should().BeTrue();
        secondStored.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task RefreshToken_CannotBeUsedTwice()
    {
        var firstPair = await LoginAsync();
        var firstUse = await RefreshAsync(firstPair.RefreshToken);
        var secondUse = await RefreshAsync(firstPair.RefreshToken);

        firstUse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondUse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredRefreshToken_IsRejected()
    {
        var rawToken = CreateRawToken();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
            var user = await db.Users.SingleAsync(
                value => value.Email == _factory.Email);
            db.RefreshTokens.Add(RefreshToken.Create(
                user.Id,
                RefreshTokenService.HashToken(rawToken),
                _factory.Clock.UtcNow.AddTicks(-1)));
            await db.SaveChangesAsync();
        }

        var response = await RefreshAsync(rawToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnknownRefreshToken_IsRejected()
    {
        var response = await RefreshAsync(CreateRawToken());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingOrBlankRefreshToken_IsRejected(string? refreshToken)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest(refreshToken));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var pair = await LoginAsync();
        var logout = await _client.PostAsJsonAsync(
            "/api/auth/logout",
            new RefreshRequest(pair.RefreshToken));
        var refresh = await RefreshAsync(pair.RefreshToken);

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_DoesNotRevealWhetherTokenExists()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/logout",
            new RefreshRequest(CreateRawToken()));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AccessToken_ExpiresAfterExactlyFifteenMinutes()
    {
        var pair = await LoginAsync();
        var token = new JwtSecurityTokenHandler().ReadJwtToken(pair.AccessToken);
        var issuedAt = long.Parse(token.Claims.Single(
            claim => claim.Type == JwtRegisteredClaimNames.Iat).Value);

        (token.Payload.Expiration!.Value - issuedAt).Should().Be(900);
        pair.ExpiresIn.Should().Be(900);
    }

    [Fact]
    public async Task RefreshToken_ExpiresAfterExactlySevenDays()
    {
        var pair = await LoginAsync();
        var tokenHash = RefreshTokenService.HashToken(pair.RefreshToken);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        var stored = await db.RefreshTokens.SingleAsync(
            token => token.Token == tokenHash);

        (stored.ExpiresAt - _factory.Clock.UtcNow)
            .Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public async Task ReusingRotatedRefreshToken_RevokesEntireChain()
    {
        var firstPair = await LoginAsync();
        var secondPair = await ReadPairAsync(
            await RefreshAsync(firstPair.RefreshToken));
        var thirdPair = await ReadPairAsync(
            await RefreshAsync(secondPair.RefreshToken));

        var reuseResponse = await RefreshAsync(firstPair.RefreshToken);
        var activeReplacementResponse = await RefreshAsync(thirdPair.RefreshToken);

        reuseResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        activeReplacementResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var chainHashes = new[]
        {
            RefreshTokenService.HashToken(firstPair.RefreshToken),
            RefreshTokenService.HashToken(secondPair.RefreshToken),
            RefreshTokenService.HashToken(thirdPair.RefreshToken)
        };
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        var storedChain = await db.RefreshTokens
            .Where(token => chainHashes.Contains(token.Token))
            .ToListAsync();

        storedChain.Should().HaveCount(3);
        storedChain.All(token => token.RevokedAt is not null).Should().BeTrue();
    }

    private async Task<TokenPair> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = _factory.Email, password = _factory.Password });
        return await ReadPairAsync(response);
    }

    private Task<HttpResponseMessage> RefreshAsync(string rawToken) =>
        _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest(rawToken));

    private static async Task<TokenPair> ReadPairAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenPair>())!;
    }

    private static string CreateRawToken() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    private sealed record RefreshRequest(
        [property: JsonPropertyName("refresh_token")] string? RefreshToken);

    private sealed record TokenPair(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
