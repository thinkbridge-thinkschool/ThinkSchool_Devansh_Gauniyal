namespace FastApi;

// IDENTICAL deterministic seed and volumes to task-1's PerformanceSeeder
// (day-3/task-3/QuotesApi/Performance/PerformanceSeeder.cs): same author count, same
// quotes-per-author, same naming pattern, same country list, same base date. A different
// data volume would invalidate the before/after comparison.
public static class Seeder
{
    public const int AuthorCount = 50;
    public const int QuotesPerAuthor = 100;
    public const int QuoteCount = AuthorCount * QuotesPerAuthor;

    private static readonly string[] Countries =
    {
        "Synthland", "Testonia", "Fixturia", "Mockavia", "Stubland", "Sampleria"
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

        var baseDate = new DateTime(2026, 1, 1);
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
