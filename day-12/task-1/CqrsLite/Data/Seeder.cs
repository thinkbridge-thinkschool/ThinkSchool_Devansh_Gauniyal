using CqrsLite.Domain;

namespace CqrsLite.Data;

// Deterministic, obviously-synthetic seed data. No real people, no real quotations
// attributed to real figures. Author names are "Author 0NN" and quote text is
// "Synthetic quote text 0NNNN" - fixtures, not fiction dressed up as fact.
public static class Seeder
{
    public const int AuthorCount = 10;
    public const int QuotesPerAuthor = 6;
    public const int QuoteCount = AuthorCount * QuotesPerAuthor;

    private static readonly string[] Countries =
    {
        "Placeholderia", "Exemplaria", "Sampletown", "Testopia", "Demoland"
    };

    public static void SeedIfNeeded(QuotesDbContext context)
    {
        context.Database.EnsureCreated();

        if (context.Authors.Count() == AuthorCount && context.Quotes.Count() == QuoteCount)
        {
            return;
        }

        context.Quotes.RemoveRange(context.Quotes);
        context.Authors.RemoveRange(context.Authors);
        context.SaveChanges();

        var authors = new List<Author>(AuthorCount);
        for (int i = 1; i <= AuthorCount; i++)
        {
            authors.Add(new Author
            {
                Id = i,
                Name = $"Author {i:D3}",
                Country = Countries[i % Countries.Length]
            });
        }

        context.Authors.AddRange(authors);
        context.SaveChanges();

        var baseDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var quotes = new List<Quote>(QuoteCount);
        int quoteSequence = 1;
        foreach (var author in authors)
        {
            for (int j = 1; j <= QuotesPerAuthor; j++)
            {
                quotes.Add(new Quote
                {
                    Id = quoteSequence,
                    Text = $"Synthetic quote text {quoteSequence:D5}",
                    AuthorId = author.Id,
                    CreatedAt = baseDate.AddMinutes(quoteSequence)
                });
                quoteSequence++;
            }
        }

        context.Quotes.AddRange(quotes);
        context.SaveChanges();
    }
}
