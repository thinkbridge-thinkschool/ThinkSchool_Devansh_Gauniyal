using CqrsLite.Data;
using CqrsLite.Features.Quotes.Commands;

namespace CqrsLite.Tests;

public class CommandHandlerTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public CommandHandlerTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Valid_submission_succeeds_and_is_genuinely_persisted()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);
        var handler = new SubmitQuoteHandler(context);

        var result = handler.Handle(new SubmitQuoteCommand(1, "A brand new synthetic quote for author one"));

        Assert.True(result.Success);
        Assert.NotNull(result.QuoteId);
        Assert.Equal(SubmitQuoteFailureReason.None, result.FailureReason);

        using var freshContext = new QuotesDbContext(_fixture.DbPath);
        var persisted = freshContext.Quotes.Single(q => q.Id == result.QuoteId!.Value);
        Assert.Equal("A brand new synthetic quote for author one", persisted.Text);
        Assert.Equal(1, persisted.AuthorId);
    }

    [Fact]
    public void Empty_text_is_rejected_without_throwing()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);
        var handler = new SubmitQuoteHandler(context);

        var result = handler.Handle(new SubmitQuoteCommand(1, "   "));

        Assert.False(result.Success);
        Assert.Null(result.QuoteId);
        Assert.Equal(SubmitQuoteFailureReason.TextEmpty, result.FailureReason);
    }

    [Fact]
    public void Text_over_max_length_is_rejected_without_throwing()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);
        var handler = new SubmitQuoteHandler(context);

        var overLong = new string('x', SubmitQuoteHandler.MaxTextLength + 1);
        var result = handler.Handle(new SubmitQuoteCommand(1, overLong));

        Assert.False(result.Success);
        Assert.Null(result.QuoteId);
        Assert.Equal(SubmitQuoteFailureReason.TextTooLong, result.FailureReason);
    }

    [Fact]
    public void Unknown_author_is_rejected_without_throwing()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);
        var handler = new SubmitQuoteHandler(context);

        var result = handler.Handle(new SubmitQuoteCommand(9999, "Synthetic quote text 90099"));

        Assert.False(result.Success);
        Assert.Null(result.QuoteId);
        Assert.Equal(SubmitQuoteFailureReason.AuthorNotFound, result.FailureReason);
    }

    [Fact]
    public void Exact_duplicate_for_same_author_is_rejected_without_throwing()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);
        var handler = new SubmitQuoteHandler(context);

        // Seeded by TestDatabaseFixture: quote sequence 1 belongs to author 1.
        var result = handler.Handle(new SubmitQuoteCommand(1, "Synthetic quote text 00001"));

        Assert.False(result.Success);
        Assert.Null(result.QuoteId);
        Assert.Equal(SubmitQuoteFailureReason.DuplicateQuote, result.FailureReason);
    }

    [Fact]
    public void Same_text_for_a_different_author_is_not_a_duplicate()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);
        var handler = new SubmitQuoteHandler(context);

        // "Synthetic quote text 00001" belongs to author 1 - submitting it for author 2
        // is a different (author, text) pair, so it must succeed.
        var result = handler.Handle(new SubmitQuoteCommand(2, "Synthetic quote text 00001"));

        Assert.True(result.Success);
        Assert.Equal(SubmitQuoteFailureReason.None, result.FailureReason);
    }
}
