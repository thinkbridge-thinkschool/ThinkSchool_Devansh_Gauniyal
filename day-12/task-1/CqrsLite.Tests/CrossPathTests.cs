using CqrsLite.Data;
using CqrsLite.Features.Quotes.Commands;
using CqrsLite.Features.Quotes.Queries;

namespace CqrsLite.Tests;

// CQRS-lite's whole point: two separate code paths, ONE shared database. This proves the
// query path really does see what the command path wrote, with no sync step in between.
public class CrossPathTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public CrossPathTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Quote_submitted_via_command_path_appears_in_query_path_results()
    {
        SubmitQuoteResult commandResult;
        using (var commandContext = new QuotesDbContext(_fixture.DbPath))
        {
            var commandHandler = new SubmitQuoteHandler(commandContext);
            commandResult = commandHandler.Handle(new SubmitQuoteCommand(3, "Cross-path synthetic quote for author three"));
        }

        Assert.True(commandResult.Success);

        using var queryContext = new QuotesDbContext(_fixture.DbPath);
        var queryHandler = new QuoteWallHandler(queryContext);
        var wall = queryHandler.Handle(new QuoteWallQuery());

        var written = Assert.Single(wall, item => item.QuoteId == commandResult.QuoteId);
        Assert.Equal("Cross-path synthetic quote for author three", written.QuoteText);
        Assert.Equal("Author 003", written.AuthorName);
    }
}
