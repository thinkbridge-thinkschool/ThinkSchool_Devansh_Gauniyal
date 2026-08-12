using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Quotes.Api.Models;

namespace Quotes.Tests.Integration;

[Collection(MsSqlContainerCollection.Name)]
public sealed class QuotesEndpointTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task GetQuotes_EmptyDatabase_ReturnsEmptyCollection()
    {
        using var factory = new QuotesApiFactory(fixture);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quotes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<Quote>>())!);
    }

    [Fact]
    public async Task GetQuotes_SeededDatabase_ReturnsSeededQuote()
    {
        using var factory = new QuotesApiFactory(fixture);
        using var client = factory.CreateClient();
        await factory.SeedQuoteAsync(text: "A SQL Server quote");

        var quotes = await client.GetFromJsonAsync<List<Quote>>("/api/quotes");

        var quote = Assert.Single(quotes!);
        Assert.Equal("A SQL Server quote", quote.Text);
    }

    [Fact]
    public async Task GetQuotes_InvalidLimit_ReturnsValidationProblemDetails()
    {
        using var factory = new QuotesApiFactory(fixture);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quotes?limit=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("limit", out _));
    }

    [Fact]
    public async Task GetQuote_MissingQuote_ReturnsNotFound()
    {
        using var factory = new QuotesApiFactory(fixture);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quotes/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateQuote_ValidAuthenticatedRequest_ReturnsCreatedAndPersistsQuote()
    {
        using var factory = new QuotesApiFactory(fixture);
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest("Persisted in SQL Server"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<Quote>())!;
        Assert.True(created.Id > 0);
        Assert.Equal("Persisted in SQL Server", (await factory.FindQuoteAsync(created.Id))?.Text);
    }

    [Fact]
    public async Task CreateQuote_AnonymousRequest_ReturnsUnauthorized()
    {
        using var factory = new QuotesApiFactory(fixture);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest("Unauthorized quote"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateQuote_BlankText_ReturnsValidationProblemDetails()
    {
        using var factory = new QuotesApiFactory(fixture);
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("text", out _));
    }

    [Fact]
    public async Task CreateQuote_FakeClockTime_PersistsExactTimestamp()
    {
        var expected = new DateTimeOffset(2026, 8, 12, 10, 11, 12, 345, TimeSpan.Zero)
            .AddTicks(6789);
        using var factory = new QuotesApiFactory(fixture, expected);
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new CreateQuoteRequest("Precisely timed quote"));

        var created = (await response.Content.ReadFromJsonAsync<Quote>())!;
        Assert.Equal(expected, (await factory.FindQuoteAsync(created.Id))?.CreatedAtUtc);
    }

    [Fact]
    public async Task UpdateQuote_ExistingQuote_ReturnsOkAndPersistsChange()
    {
        using var factory = new QuotesApiFactory(fixture);
        using var client = CreateAuthenticatedClient(factory);
        var seeded = await factory.SeedQuoteAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/quotes/{seeded.Id}",
            new UpdateQuoteRequest("Updated quote"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Updated quote", (await factory.FindQuoteAsync(seeded.Id))?.Text);
    }

    [Fact]
    public async Task UpdateQuote_AnonymousRequest_ReturnsUnauthorized()
    {
        using var factory = new QuotesApiFactory(fixture);
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/api/quotes/1",
            new UpdateQuoteRequest("Unauthorized update"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_ExistingQuote_ReturnsNoContentAndDeletesQuote()
    {
        using var factory = new QuotesApiFactory(fixture);
        using var client = CreateAuthenticatedClient(factory);
        var seeded = await factory.SeedQuoteAsync();

        var response = await client.DeleteAsync($"/api/quotes/{seeded.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await factory.FindQuoteAsync(seeded.Id));
    }

    [Fact]
    public async Task DeleteQuote_MissingQuote_ReturnsNotFound()
    {
        using var factory = new QuotesApiFactory(fixture);
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.DeleteAsync("/api/quotes/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Database_Startup_AppliesSqlServerMigration()
    {
        using var factory = new QuotesApiFactory(fixture);
        using var client = factory.CreateClient();

        var migrations = await factory.GetAppliedMigrationsAsync();

        Assert.Contains(migrations, migration => migration.EndsWith("_InitialCreate"));
    }

    [Fact]
    public async Task Database_DefaultSqlServerCollation_MatchesTextCaseInsensitively()
    {
        using var factory = new QuotesApiFactory(fixture);
        using var client = factory.CreateClient();
        await factory.SeedQuoteAsync(text: "Case Sensitive To SQLite");

        var matches = await factory.HasCaseInsensitiveTextMatchAsync(
            "case sensitive to sqlite");

        Assert.True(matches);
    }

    [Fact]
    public async Task SeparateTests_UniqueDatabases_DoNotShareApplicationData()
    {
        using var firstFactory = new QuotesApiFactory(fixture);
        using var firstClient = firstFactory.CreateClient();
        await firstFactory.SeedQuoteAsync(text: "Only in the first database");

        using var secondFactory = new QuotesApiFactory(fixture);
        using var secondClient = secondFactory.CreateClient();

        Assert.NotEqual(firstFactory.DatabaseName, secondFactory.DatabaseName);
        Assert.Equal(1, await firstFactory.CountQuotesAsync());
        Assert.Equal(0, await secondFactory.CountQuotesAsync());
    }

    private static HttpClient CreateAuthenticatedClient(QuotesApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateAccessToken());
        return client;
    }
}
