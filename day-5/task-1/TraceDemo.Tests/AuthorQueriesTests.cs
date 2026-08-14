using System.Text.Json;
using TraceDemo.Data;
using TraceDemo.Queries;
using Xunit;

namespace TraceDemo.Tests;

public class AuthorQueriesTests
{
    [Fact]
    public async Task NPlusOne_IssuesAuthorCountPlusOneRoundTrips()
    {
        using var db = TestDatabase.CreateSeeded();

        await AuthorQueries.GetAuthorsNPlusOneAsync(db.Context, CancellationToken.None);

        Assert.Equal(SeedData.AuthorCount + 1, db.Interceptor.Count);
    }

    [Fact]
    public async Task SingleQuery_IssuesExactlyOneRoundTrip()
    {
        using var db = TestDatabase.CreateSeeded();

        await AuthorQueries.GetAuthorsSingleQueryAsync(db.Context, CancellationToken.None);

        Assert.Equal(1, db.Interceptor.Count);
    }

    // The key regression test: both endpoints return byte-identical JSON, so only a
    // round-trip count can catch a slide back into N+1 -- a data-only assertion would
    // stay green even if someone quietly reintroduced the per-author loop.
    [Fact]
    public async Task BothMethods_ReturnIdenticalData_ButSingleQueryUsesFewerRoundTrips()
    {
        using var nPlusOneDb = TestDatabase.CreateSeeded();
        using var singleQueryDb = TestDatabase.CreateSeeded();

        var nPlusOneResult = await AuthorQueries.GetAuthorsNPlusOneAsync(nPlusOneDb.Context, CancellationToken.None);
        var nPlusOneRoundTrips = nPlusOneDb.Interceptor.Count;

        var singleQueryResult =
            await AuthorQueries.GetAuthorsSingleQueryAsync(singleQueryDb.Context, CancellationToken.None);
        var singleQueryRoundTrips = singleQueryDb.Interceptor.Count;

        var nPlusOneJson = JsonSerializer.Serialize(nPlusOneResult);
        var singleQueryJson = JsonSerializer.Serialize(singleQueryResult);

        Assert.Equal(nPlusOneJson, singleQueryJson);
        Assert.True(
            singleQueryRoundTrips < nPlusOneRoundTrips,
            $"Expected single-query round trips ({singleQueryRoundTrips}) to be fewer than N+1 round trips ({nPlusOneRoundTrips}).");
    }

    [Fact]
    public void Seed_ProducesExpectedAuthorsAndBooks()
    {
        using var db = TestDatabase.CreateSeeded();

        Assert.Equal(SeedData.AuthorCount, db.Context.Authors.Count());
        Assert.Equal(SeedData.AuthorCount * SeedData.BooksPerAuthor, db.Context.Books.Count());
        Assert.All(
            db.Context.Authors,
            author => Assert.Equal(SeedData.BooksPerAuthor, db.Context.Books.Count(b => b.AuthorId == author.Id)));
    }

    [Fact]
    public async Task EmptyDatabase_BothMethodsReturnEmpty()
    {
        using var db = TestDatabase.CreateEmpty();

        var nPlusOneResult = await AuthorQueries.GetAuthorsNPlusOneAsync(db.Context, CancellationToken.None);
        var singleQueryResult = await AuthorQueries.GetAuthorsSingleQueryAsync(db.Context, CancellationToken.None);

        Assert.Empty(nPlusOneResult);
        Assert.Empty(singleQueryResult);
    }

    [Fact]
    public async Task AuthorWithNoBooks_BothMethodsAgreeOnEmptyTitleList()
    {
        using var db = TestDatabase.CreateEmpty();
        db.Context.Authors.Add(new Author { Name = "Lonely Author" });
        db.Context.SaveChanges();
        db.Interceptor.Reset();

        var nPlusOneResult = await AuthorQueries.GetAuthorsNPlusOneAsync(db.Context, CancellationToken.None);
        var singleQueryResult = await AuthorQueries.GetAuthorsSingleQueryAsync(db.Context, CancellationToken.None);

        Assert.Empty(Assert.Single(nPlusOneResult).BookTitles);
        Assert.Empty(Assert.Single(singleQueryResult).BookTitles);
    }

    [Fact]
    public async Task Cancellation_IsHonoured_ForBothMethods()
    {
        using var db = TestDatabase.CreateSeeded();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AuthorQueries.GetAuthorsNPlusOneAsync(db.Context, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AuthorQueries.GetAuthorsSingleQueryAsync(db.Context, cts.Token));
    }
}
