using CqrsLite.Data;
using CqrsLite.Domain;

namespace CqrsLite.Features.Quotes.Commands;

public sealed class SubmitQuoteHandler
{
    public const int MaxTextLength = 280;

    private readonly QuotesDbContext _context;

    public SubmitQuoteHandler(QuotesDbContext context)
    {
        _context = context;
    }

    public SubmitQuoteResult Handle(SubmitQuoteCommand command)
    {
        var text = command.Text?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            return SubmitQuoteResult.Fail(SubmitQuoteFailureReason.TextEmpty);
        }

        if (text.Length > MaxTextLength)
        {
            return SubmitQuoteResult.Fail(SubmitQuoteFailureReason.TextTooLong);
        }

        var author = _context.Authors.SingleOrDefault(a => a.Id == command.AuthorId);
        if (author is null)
        {
            return SubmitQuoteResult.Fail(SubmitQuoteFailureReason.AuthorNotFound);
        }

        var isDuplicate = _context.Quotes
            .Any(q => q.AuthorId == command.AuthorId && q.Text == text);
        if (isDuplicate)
        {
            return SubmitQuoteResult.Fail(SubmitQuoteFailureReason.DuplicateQuote);
        }

        var quote = new Quote
        {
            Text = text,
            AuthorId = command.AuthorId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Quotes.Add(quote);
        _context.SaveChanges();

        return SubmitQuoteResult.Ok(quote.Id);
    }
}
