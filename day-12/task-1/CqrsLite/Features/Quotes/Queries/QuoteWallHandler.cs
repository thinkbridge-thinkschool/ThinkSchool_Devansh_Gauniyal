using CqrsLite.Data;
using Microsoft.EntityFrameworkCore;

namespace CqrsLite.Features.Quotes.Queries;

public sealed class QuoteWallHandler
{
    private readonly QuotesDbContext _context;

    public QuoteWallHandler(QuotesDbContext context)
    {
        _context = context;
    }

    public List<QuoteWallItem> Handle(QuoteWallQuery query)
    {
        return _context.Quotes
            .AsNoTracking()
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
