using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace QuotesApi.Tests;

public sealed class AuthIntegrationTests
{
    [Fact]
    public async Task Anonymous_ProtectedEndpoint_ReturnsUnauthorized()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedWithoutRequiredPolicy_ReturnsForbidden()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateJsonRequest(
            HttpMethod.Put,
            "/api/quotes/1",
            new { text = "Updated" },
            factory.CreateInternalToken(scope: null));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedWithRequiredPolicy_ReturnsOk()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateJsonRequest(
            HttpMethod.Put,
            "/api/quotes/1",
            new { text = "Updated" },
            factory.CreateInternalToken());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredAccessToken_ReturnsUnauthorized()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/protected",
            factory.CreateInternalToken(expires: DateTime.UtcNow.AddMinutes(-5)));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReusedRefreshToken_RevokesFamilyAndReturnsUnauthorized()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var firstPair = await LoginAsync(client, factory);
        var secondPair = await ReadPairAsync(
            await RefreshAsync(client, firstPair.RefreshToken));

        var reuse = await RefreshAsync(client, firstPair.RefreshToken);
        var descendant = await RefreshAsync(client, secondPair.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, descendant.StatusCode);
    }

    [Fact]
    public async Task ValidInternalJwt_ReturnsOk()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/protected",
            factory.CreateInternalToken());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ValidEntraStyleTestJwt_ReturnsOk()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/protected",
            factory.CreateEntraToken());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MalformedJwt_ReturnsUnauthorizedInsteadOfServerError()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/protected",
            "not.a.valid-token");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongIssuer_ReturnsUnauthorized()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/protected",
            factory.CreateInternalToken(issuer: "unexpected-issuer"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongAudience_ReturnsUnauthorized()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/protected",
            factory.CreateInternalToken(audience: "unexpected-audience"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NonOwnerDelete_ReturnsForbidden()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Delete,
            "/api/quotes/1",
            factory.CreateInternalToken(userId: "user-2"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OwnerDelete_ReturnsOk()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Delete,
            "/api/quotes/1",
            factory.CreateInternalToken(userId: "user-1"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/quotes")]
    [InlineData("PUT", "/api/quotes/1")]
    [InlineData("DELETE", "/api/quotes/1")]
    public async Task AnonymousQuoteMutation_ReturnsUnauthorized(
        string method,
        string route)
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method != "DELETE")
        {
            request.Content = JsonContent.Create(new { text = "New text" });
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateQuote_WithWritePolicy_ReturnsOk()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "/api/quotes",
            new { text = "A new quote" },
            factory.CreateEntraToken());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateQuote_WithoutWritePolicy_ReturnsForbidden()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "/api/quotes",
            new { text = "A new quote" },
            factory.CreateInternalToken(scope: null));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Login_IssuesAccessTokenThatAuthenticatesSuccessfully()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();
        var pair = await LoginAsync(client, factory);
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/protected",
            pair.AccessToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(pair.RefreshToken));
        Assert.Equal(900, pair.ExpiresIn);
    }

    [Fact]
    public async Task UnknownRefreshToken_ReturnsUnauthorized()
    {
        using var factory = new AuthApiFactory();
        using var client = factory.CreateClient();

        var response = await RefreshAsync(client, "unknown-refresh-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<TokenPair> LoginAsync(
        HttpClient client,
        AuthApiFactory factory)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = factory.Email, password = factory.Password });
        return await ReadPairAsync(response);
    }

    private static Task<HttpResponseMessage> RefreshAsync(
        HttpClient client,
        string refreshToken) =>
        client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest(refreshToken));

    private static async Task<TokenPair> ReadPairAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenPair>())!;
    }

    private static HttpRequestMessage CreateJsonRequest(
        HttpMethod method,
        string route,
        object body,
        string token)
    {
        var request = CreateAuthenticatedRequest(method, route, token);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string route,
        string token)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed record RefreshRequest(
        [property: JsonPropertyName("refresh_token")] string RefreshToken);

    private sealed record TokenPair(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
