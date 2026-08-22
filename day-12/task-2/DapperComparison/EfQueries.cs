using Microsoft.EntityFrameworkCore;

namespace DapperComparison;

public static class EfQueries
{
    public static List<QuoteWallItem> RunTracked(QuotesDbContext context, DateTime submittedSinceUtc)
    {
        var quotes = context.Quotes
            .Include(q => q.Author)
            .Where(q => q.CreatedAt >= submittedSinceUtc)
            .OrderByDescending(q => q.CreatedAt)
            .ThenByDescending(q => q.Id)
            .ToList();

        return quotes.Select(q => new QuoteWallItem
        {
            QuoteId = q.Id,
            QuoteText = q.Text,
            AuthorName = q.Author!.Name,
            AuthorCountry = q.Author!.Country,
            SubmittedOn = q.CreatedAt.ToString("yyyy-MM-dd")
        }).ToList();
    }

    public static List<QuoteWallItem> RunProjection(QuotesDbContext context, DateTime submittedSinceUtc)
    {
        return context.Quotes
            .AsNoTracking()
            .Where(q => q.CreatedAt >= submittedSinceUtc)
            .OrderByDescending(q => q.CreatedAt)
            .ThenByDescending(q => q.Id)
            .Select(q => new QuoteWallItem
            {
                QuoteId = q.Id,
                QuoteText = q.Text,
                AuthorName = q.Author!.Name,
                AuthorCountry = q.Author!.Country,
                SubmittedOn = q.CreatedAt.ToString("yyyy-MM-dd")
            })
            .ToList();
    }
}
