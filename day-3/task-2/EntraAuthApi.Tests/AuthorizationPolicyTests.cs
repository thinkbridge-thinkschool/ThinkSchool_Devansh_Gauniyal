using System.Net;
using System.Net.Http.Json;

namespace EntraAuthApi.Tests;

public sealed class AuthorizationPolicyTests : IClassFixture<AuthorizationApiFactory>
{
    private readonly HttpClient _client;

    public AuthorizationPolicyTests(AuthorizationApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task EditQuote_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var request = CreateEditRequest();

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EditQuote_AuthenticatedWithoutWriteScope_ReturnsForbidden()
    {
        using var request = CreateEditRequest(userId: "user-1");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EditQuote_AuthenticatedWithWriteScope_ReturnsOk()
    {
        using var request = CreateEditRequest(
            userId: "user-1",
            scope: "quotes.write");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_WithoutAuthentication_ReturnsUnauthorized()
    {
        var response = await _client.DeleteAsync("/api/quotes/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_AuthenticatedNonOwner_ReturnsForbidden()
    {
        using var request = CreateDeleteRequest(quoteId: 1, userId: "user-2");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_AuthenticatedOwner_ReturnsNoContent()
    {
        using var request = CreateDeleteRequest(quoteId: 1, userId: "user-1");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_AuthenticatedMissingQuote_ReturnsNotFound()
    {
        using var request = CreateDeleteRequest(quoteId: 999, userId: "user-1");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static HttpRequestMessage CreateEditRequest(
        string? userId = null,
        string? scope = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/quotes/1")
        {
            Content = JsonContent.Create(new { text = "Updated quote" })
        };

        AddTestIdentity(request, userId, scope);
        return request;
    }

    private static HttpRequestMessage CreateDeleteRequest(int quoteId, string userId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/quotes/{quoteId}");
        AddTestIdentity(request, userId);
        return request;
    }

    private static void AddTestIdentity(
        HttpRequestMessage request,
        string? userId,
        string? scope = null)
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            request.Headers.Add(TestAuthenticationHandler.UserHeader, userId);
        }

        if (!string.IsNullOrWhiteSpace(scope))
        {
            request.Headers.Add(TestAuthenticationHandler.ScopeHeader, scope);
        }
    }
}
