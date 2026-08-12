# Day 3 — Task 6: Integration tests with WebApplicationFactory

## 1. GitHub link

NOT YET AVAILABLE

## 2. Required mentor notes/deliverables

The test suite uses SQLite in memory, applies the genuine EF Core migration, replaces `IClock` with a fake, and gives every test a fresh database and `HttpClient`.

## 3. WebApplicationFactory subclass

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Quotes.Api.Data;
using Quotes.Api.Models;
using Quotes.Api.Time;

namespace Quotes.Tests.Integration;

public sealed class QuotesApiFactory : WebApplicationFactory<Program>
{
    private const string TestIssuer = "quotes-api.integration-tests";
    private const string TestAudience = "quotes-api.integration-clients";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string _signingKey = Convert.ToBase64String(
        RandomNumberGenerator.GetBytes(32));

    public QuotesApiFactory(DateTimeOffset? utcNow = null)
    {
        Clock = new FakeClock(
            utcNow ?? new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero));
        _connection.Open();
    }

    public FakeClock Clock { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Authentication:Issuer", TestIssuer);
        builder.UseSetting("Authentication:Audience", TestAudience);
        builder.UseSetting("Authentication:SigningKey", _signingKey);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<QuotesDbContext>();
            services.RemoveAll<DbContextOptions<QuotesDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<QuotesDbContext>>();
            services.AddDbContext<QuotesDbContext>(options =>
                options.UseSqlite(_connection));

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        database.Database.Migrate();

        return host;
    }

    public string CreateAccessToken(string userId = "integration-user")
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, userId)],
            notBefore: now.AddMinutes(-5),
            expires: now.AddMinutes(30),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<Quote> SeedQuoteAsync(
        string ownerId = "seed-owner",
        string text = "Seeded quote")
    {
        using var scope = Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        var quote = new Quote
        {
            OwnerId = ownerId,
            Text = text,
            CreatedAtUtc = Clock.UtcNow
        };

        database.Quotes.Add(quote);
        await database.SaveChangesAsync();

        return quote;
    }

    public async Task<IReadOnlyList<string>> GetAppliedMigrationsAsync()
    {
        using var scope = Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        return (await database.Database.GetAppliedMigrationsAsync()).ToList();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
```

## 4. Happy-path integration test

```csharp
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
```

## 5. Error-path integration test

```csharp
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
```

## 6. Genuine test command

Working directory: `/Users/devansh/thinkschool/day-3/task-6`

```text
dotnet test Task6.slnx --no-build --verbosity normal
```

## 7. Genuine test output

```text
Test Run Successful.
Total tests: 22
     Passed: 22
 Total time: 2.2204 Seconds

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.65
```

## 8. Test-isolation explanation

Each test creates and disposes its own `QuotesApiFactory` and `HttpClient`. Every factory opens a separate SQLite `Data Source=:memory:` connection, keeps it open for that factory's lifetime, applies migrations to that fresh database, and seeds only the data required by its test.

## 9. What did you learn this session?

I learned how `WebApplicationFactory` runs the real ASP.NET Core pipeline through an in-memory TestServer. Replacing only the database and clock makes integration tests deterministic while still verifying routing, validation, dependency injection, EF Core and authentication together.

## 10. What would break this?

The tests could become unreliable if they shared one mutable database or used the real system clock for quote creation. Closing the in-memory SQLite connection too early would also delete the database before the test completed.
