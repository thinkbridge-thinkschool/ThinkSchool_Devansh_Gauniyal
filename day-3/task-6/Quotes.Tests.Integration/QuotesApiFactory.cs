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
