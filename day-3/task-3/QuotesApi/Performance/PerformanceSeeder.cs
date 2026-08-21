namespace QuotesApi.Performance;

public static class PerformanceSeeder
{
    public const int AuthorCount = 50;
    public const int QuotesPerAuthor = 100;
    public const int QuoteCount = AuthorCount * QuotesPerAuthor;

    private static readonly string[] Countries =
    {
        "Synthland", "Testonia", "Fixturia", "Mockavia", "Stubland", "Sampleria"
    };

    // Deterministic (no wall-clock, no randomness) and safely re-runnable: if the tables
    // don't already hold exactly AuthorCount / QuoteCount rows, they are cleared and
    // reseeded from scratch. Every name, country and quote text below is synthetic.
    public static void SeedIfNeeded(PerformanceDbContext context)
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

        var baseDate = new DateTime(2026, 1, 1);
        var quotes = new List<AuthorQuote>(QuoteCount);
        int quoteSequence = 1;
        foreach (var author in authors)
        {
            for (int j = 1; j <= QuotesPerAuthor; j++)
            {
                quotes.Add(new AuthorQuote
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
