using Microsoft.EntityFrameworkCore;

namespace FastApi;

public static class Queries
{
    // Reproduced exactly from task-1's AuthorQuoteSummaryQuery.Run
    // (day-3/task-3/QuotesApi/Performance/AuthorQuoteSummaryQuery.cs): 1 query for the
    // authors, then one explicit per-author Load() inside the loop - 1 + N round trips.
    // Kept here unmodified so the in-process tests can prove the fix, not a changed
    // workload, is what makes the difference.
    public static List<AuthorWithQuotesDto> RunSlow(QuotesDbContext context)
    {
        var authors = context.Authors.OrderBy(a => a.Id).ToList(); // query 1

        var summaries = new List<AuthorWithQuotesDto>(authors.Count);
        foreach (var author in authors)
        {
            context.Entry(author).Collection(a => a.Quotes).Load(); // query 2..N+1
            summaries.Add(new AuthorWithQuotesDto(author.Id, author.Name, author.Country, author.Quotes.Count));
        }

        return summaries;
    }

    // FIX 1 - projection: a single Select into AuthorWithQuotesDto. a.Quotes.Count()
    // translates to a correlated scalar subquery per author row, so the whole thing is
    // ONE SQL statement - no Include(), nothing loaded into the change tracker (a
    // projection to a non-entity type is never tracked), connecting back to Day 10 Task 1.
    public static List<AuthorWithQuotesDto> RunProjection(QuotesDbContext context)
    {
        return context.Authors
            .OrderBy(a => a.Id)
            .Select(a => new AuthorWithQuotesDto(a.Id, a.Name, a.Country, a.Quotes.Count()))
            .ToList();
    }

    // FIX 2 - Include with split queries: Include(a => a.Quotes) alone would produce a
    // single JOIN query that repeats every author row once per quote (50 authors x 100
    // quotes each = a 5,000-row result set just to describe 50 authors) - the cartesian
    // explosion problem. AsSplitQuery() instead issues two compact queries (authors, then
    // their quotes via a second query correlated on the buffered author IDs) - a small
    // FIXED number of round trips that does not grow with N, instead of one bloated one.
    public static List<AuthorWithQuotesDto> RunSplitQuery(QuotesDbContext context)
    {
        var authors = context.Authors
            .AsNoTracking()
            .Include(a => a.Quotes)
            .AsSplitQuery()
            .OrderBy(a => a.Id)
            .ToList();

        return authors
            .Select(a => new AuthorWithQuotesDto(a.Id, a.Name, a.Country, a.Quotes.Count))
            .ToList();
    }
}
