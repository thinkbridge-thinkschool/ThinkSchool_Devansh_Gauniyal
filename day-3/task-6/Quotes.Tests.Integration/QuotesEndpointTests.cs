using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Quotes.Api.Models;

namespace Quotes.Tests.Integration;

public sealed class QuotesEndpointTests
{
    [Fact]
    public async Task GetQuotes_EmptyDatabase_ReturnsOkWithEmptyCollection()
    {
        await using var factory = new QuotesApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quotes");
        var quotes = await response.Content.ReadFromJsonAsync<List<Quote>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(quotes);
        Assert.Empty(quotes);
    }

    [Fact]
    public async Task GetQuotes_SeededDatabase_ReturnsStoredQuotes()
    {
        await using var factory = new QuotesApiFactory();
        await factory.SeedQuoteAsync(text: "Stored through EF Core");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quotes");
        var quotes = await response.Content.ReadFromJsonAsync<List<Quote>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = Assert.Single(quotes!);
        Assert.Equal("Stored through EF Core", quote.Text);
    }

    [Fact]
    public async Task GetQuotes_InvalidLimit_ReturnsValidationProblemDetails()
    {
        await using var factory = new QuotesApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quotes?limit=0");
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(StatusCodes.Status400BadRequest, problem!.Status);
        Assert.Equal("One or more validation errors occurred.", problem.Title);
        Assert.Contains("limit", problem.Errors.Keys);
    }

    [Fact]
    public async Task GetQuote_ExistingId_ReturnsOk()
    {
        await using var factory = new QuotesApiFactory();
        var seeded = await factory.SeedQuoteAsync(text: "Find this quote");
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/quotes/{seeded.Id}");
        var quote = await response.Content.ReadFromJsonAsync<Quote>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(seeded.Id, quote!.Id);
        Assert.Equal("Find this quote", quote.Text);
    }

    [Fact]
    public async Task GetQuote_MissingId_ReturnsNotFound()
    {
        await using var factory = new QuotesApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quotes/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateQuote_ValidAuthenticatedRequest_ReturnsCreatedAndPersistsQuote()
    {
        await using var factory = new QuotesApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateAccessToken("quote-owner"));

        var createResponse = await client.PostAsJsonAsync(
            "/api/quotes",
            new { Text = "Persisted through the real HTTP pipeline" });
        var created = await createResponse.Content.ReadFromJsonAsync<Quote>();
        var getResponse = await client.GetAsync($"/api/quotes/{created!.Id}");
        var stored = await getResponse.Content.ReadFromJsonAsync<Quote>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal($"/api/quotes/{created.Id}", createResponse.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("quote-owner", stored!.OwnerId);
        Assert.Equal("Persisted through the real HTTP pipeline", stored.Text);
    }

    [Fact]
    public async Task CreateQuote_AnonymousRequest_ReturnsUnauthorized()
    {
        await using var factory = new QuotesApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { Text = "Authentication is required" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateQuote_TextIsMissing_ReturnsValidationProblemDetails(string? text)
    {
        await using var factory = new QuotesApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateAccessToken());

        var response = await client.PostAsJsonAsync("/api/quotes", new { Text = text });
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(StatusCodes.Status400BadRequest, problem!.Status);
        Assert.Equal("One or more validation errors occurred.", problem.Title);
        Assert.Equal("Quote text is required.", Assert.Single(problem.Errors["text"]));
    }

    [Fact]
    public async Task CreateQuote_TextTooLong_ReturnsValidationProblemDetails()
    {
        await using var factory = new QuotesApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateAccessToken());

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { Text = new string('a', 281) });
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(StatusCodes.Status400BadRequest, problem!.Status);
        Assert.Equal("Quote text cannot exceed 280 characters.", Assert.Single(problem.Errors["text"]));
    }

    [Fact]
    public async Task CreateQuote_InvalidToken_ReturnsUnauthorized()
    {
        await using var factory = new QuotesApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "clearly-invalid-synthetic-token");

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { Text = "This must not be created" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateQuote_FakeClockTime_PersistsExpectedCreatedAt()
    {
        var expectedTime = new DateTimeOffset(2032, 4, 5, 6, 7, 8, TimeSpan.Zero);
        await using var factory = new QuotesApiFactory(expectedTime);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateAccessToken());

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { Text = "Created at a deterministic time" });
        var quote = await response.Content.ReadFromJsonAsync<Quote>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(expectedTime, quote!.CreatedAtUtc);
    }

    [Fact]
    public async Task UpdateQuote_ExistingQuoteWithValidToken_ReturnsOkAndPersistsChange()
    {
        await using var factory = new QuotesApiFactory();
        var seeded = await factory.SeedQuoteAsync(text: "Before update");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateAccessToken());

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/quotes/{seeded.Id}",
            new { Text = "After update" });
        var getResponse = await client.GetAsync($"/api/quotes/{seeded.Id}");
        var stored = await getResponse.Content.ReadFromJsonAsync<Quote>();

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal("After update", stored!.Text);
    }

    [Fact]
    public async Task UpdateQuote_MissingQuote_ReturnsNotFound()
    {
        await using var factory = new QuotesApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateAccessToken());

        var response = await client.PutAsJsonAsync(
            "/api/quotes/999",
            new { Text = "Nothing to update" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateQuote_AnonymousRequest_ReturnsUnauthorized()
    {
        await using var factory = new QuotesApiFactory();
        var seeded = await factory.SeedQuoteAsync();
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/quotes/{seeded.Id}",
            new { Text = "Anonymous update" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateQuote_BlankText_ReturnsValidationProblemDetails()
    {
        await using var factory = new QuotesApiFactory();
        var seeded = await factory.SeedQuoteAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateAccessToken());

        var response = await client.PutAsJsonAsync(
            $"/api/quotes/{seeded.Id}",
            new { Text = "   " });
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Quote text is required.", Assert.Single(problem!.Errors["text"]));
    }

    [Fact]
    public async Task DeleteQuote_ExistingQuoteWithValidToken_ReturnsNoContent()
    {
        await using var factory = new QuotesApiFactory();
        var seeded = await factory.SeedQuoteAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateAccessToken());

        var deleteResponse = await client.DeleteAsync($"/api/quotes/{seeded.Id}");
        var getResponse = await client.GetAsync($"/api/quotes/{seeded.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_MissingQuote_ReturnsNotFound()
    {
        await using var factory = new QuotesApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateAccessToken());

        var response = await client.DeleteAsync("/api/quotes/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_AnonymousRequest_ReturnsUnauthorized()
    {
        await using var factory = new QuotesApiFactory();
        var seeded = await factory.SeedQuoteAsync();
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync($"/api/quotes/{seeded.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Migration_FreshDatabase_AppliesInitialCreateMigration()
    {
        await using var factory = new QuotesApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quotes");
        var migrations = await factory.GetAppliedMigrationsAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            migrations,
            migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetQuotes_SeparateFactories_DoNotShareDatabaseState()
    {
        await using var firstFactory = new QuotesApiFactory();
        await firstFactory.SeedQuoteAsync();
        using var firstClient = firstFactory.CreateClient();
        await using var secondFactory = new QuotesApiFactory();
        using var secondClient = secondFactory.CreateClient();

        var firstQuotes = await firstClient.GetFromJsonAsync<List<Quote>>("/api/quotes");
        var secondQuotes = await secondClient.GetFromJsonAsync<List<Quote>>("/api/quotes");

        Assert.Single(firstQuotes!);
        Assert.Empty(secondQuotes!);
    }
}
