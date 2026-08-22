namespace CqrsLite.Features.Quotes.Commands;

public sealed record SubmitQuoteCommand(int AuthorId, string Text);
