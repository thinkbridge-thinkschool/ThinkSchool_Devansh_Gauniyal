using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Queries;

public static class AuthorQueries
{
    // BEFORE FIX: deliberate N+1. One query for the author list, then one MORE query per
    // author for that author's books -- a real, separate round trip each time, not
    // simulated. For 30 seeded authors this is 31 round trips total. Kept as the
    // regression-test fixture even after the endpoint is fixed to call the method below,
    // so the round-trip-counting test keeps proving the anti-pattern would be caught.
    public static async Task<List<AuthorSummary>> GetAuthorsNPlusOneAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var authors = await db.Authors
            .AsNoTracking()
            .OrderBy(a => a.Id)
            .ToListAsync(cancellationToken);

        var result = new List<AuthorSummary>(authors.Count);
        foreach (var author in authors)
        {
            var titles = await db.Books
                .AsNoTracking()
                .Where(b => b.AuthorId == author.Id)
                .OrderBy(b => b.Id)
                .Select(b => b.Title)
                .ToListAsync(cancellationToken);

            result.Add(new AuthorSummary(author.Id, author.Name, titles));
        }

        return result;
    }

    // Fix: Include the Books navigation so EF Core's default single-query strategy
    // fetches every author and every book in ONE round trip via a join, instead of one
    // query per author.
    public static async Task<List<AuthorSummary>> GetAuthorsSingleQueryAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var authors = await db.Authors
            .AsNoTracking()
            .Include(a => a.Books)
            .OrderBy(a => a.Id)
            .ToListAsync(cancellationToken);

        return authors
            .Select(a => new AuthorSummary(
                a.Id,
                a.Name,
                a.Books.OrderBy(b => b.Id).Select(b => b.Title).ToList()))
            .ToList();
    }
}
