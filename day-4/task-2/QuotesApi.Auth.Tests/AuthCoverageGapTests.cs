using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace QuotesApi.Auth.Tests;

public sealed class AuthCoverageGapTests
{
    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = factory.Email, password = "definitely-the-wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownEmail_ReturnsUnauthorized()
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "not-the-configured-caller@example.test", password = factory.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null, "irrelevant")]
    [InlineData("irrelevant@example.test", null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public async Task Login_MissingCredentials_ReturnsUnauthorized(string? email, string? password)
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetQuotes_Anonymous_ReturnsOkWithAllQuotes()
    {
        // GET /api/quotes currently has no .RequireAuthorization() in Program.cs, unlike every
        // other /api/quotes endpoint. This test documents the actual, current behavior (public
        // read) rather than asserting it is the intended or secure design -- see README.md.
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quotes");
        var quotes = await response.Content.ReadFromJsonAsync<List<QuoteDto>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(quotes!);
    }

    [Fact]
    public async Task CreateQuote_TokenWithWritePolicyButNoSubjectClaim_ReturnsForbidden()
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();
        var token = factory.CreateToken(includeSubClaim: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes")
        {
            Content = JsonContent.Create(new { text = "New text" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateQuote_UnknownId_ReturnsNotFound()
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();
        var token = factory.CreateToken();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/quotes/999999")
        {
            Content = JsonContent.Create(new { text = "Updated" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_UnknownId_ReturnsNotFound()
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();
        var token = factory.CreateToken();
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/quotes/999999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_TokenWithNoSubjectClaim_ReturnsForbidden()
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();
        var token = factory.CreateToken(includeSubClaim: false);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/quotes/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_EmptyToken_ReturnsUnauthorized()
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refresh_token = "" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WhitespaceToken_ReturnsUnauthorized()
    {
        using var factory = new AuthCoverageApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { refresh_token = "   " });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record QuoteDto(int Id, string OwnerId, string Text);
}
