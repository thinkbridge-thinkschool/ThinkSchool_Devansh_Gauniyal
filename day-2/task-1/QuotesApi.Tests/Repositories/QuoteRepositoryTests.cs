using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Tests.Fakes;

namespace QuotesApi.Tests.Repositories;

public class QuoteRepositoryTests
{
    [Fact]
    public async Task CreateAsync_UsesTimeFromInjectedClock()
    {
        var fixedUtcNow = new DateTimeOffset(
            2026, 8, 11, 9, 30, 0, TimeSpan.Zero);
        var fakeClock = new FakeClock(fixedUtcNow);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new QuotesDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var repository = new QuoteRepository(db, fakeClock);

        var createdQuote = await repository.CreateAsync(new Quote
        {
            Author = "Grace Hopper",
            Text = "The most dangerous phrase is: we have always done it this way."
        });

        Assert.Equal(fixedUtcNow, createdQuote.CreatedAtUtc);

        var storedQuote = await db.Quotes.SingleAsync();
        Assert.Equal(fixedUtcNow, storedQuote.CreatedAtUtc);
    }
}
