namespace CqrsLite.Features.Quotes.Commands;

// The write side's input shape: exactly what submitting a quote requires, nothing a screen
// might want to display. No author name, no country, no formatted date - those belong to
// the read model, not here.
public sealed record SubmitQuoteCommand(int AuthorId, string Text);
