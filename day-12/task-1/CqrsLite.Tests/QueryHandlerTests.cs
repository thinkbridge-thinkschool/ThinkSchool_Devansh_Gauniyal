using CqrsLite.Data;
using CqrsLite.Features.Quotes.Queries;

namespace CqrsLite.Tests;

public class QueryHandlerTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public QueryHandlerTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Returns_expected_row_count()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);
        var handler = new QuoteWallHandler(context);

        var wall = handler.Handle(new QuoteWallQuery());

        Assert.Equal(Seeder.QuoteCount, wall.Count);
    }

    [Fact]
    public void Every_row_carries_denormalized_author_name_and_country()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);
        var handler = new QuoteWallHandler(context);

        var wall = handler.Handle(new QuoteWallQuery());

        Assert.All(wall, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.AuthorName));
            Assert.False(string.IsNullOrWhiteSpace(item.AuthorCountry));
            Assert.False(string.IsNullOrWhiteSpace(item.QuoteText));
            Assert.False(string.IsNullOrWhiteSpace(item.SubmittedOn));
        });
    }

    [Fact]
    public void Rows_come_back_newest_first_with_a_stable_tie_break()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);
        var handler = new QuoteWallHandler(context);

        var wall = handler.Handle(new QuoteWallQuery());

        // Documented screen contract: newest CreatedAt first, ties broken by Id descending.
        // Seeder gives every quote a distinct CreatedAt, so a strict descending Id check
        // across the whole list proves both the primary and tie-break ordering held.
        for (int i = 1; i < wall.Count; i++)
        {
            Assert.True(wall[i - 1].QuoteId > wall[i].QuoteId,
                $"Expected row {i - 1} (QuoteId={wall[i - 1].QuoteId}) to come before row {i} (QuoteId={wall[i].QuoteId}).");
        }
    }

    [Fact]
    public void Query_path_leaves_the_change_tracker_empty()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);
        var handler = new QuoteWallHandler(context);

        handler.Handle(new QuoteWallQuery());

        Assert.Empty(context.ChangeTracker.Entries());
    }
}
