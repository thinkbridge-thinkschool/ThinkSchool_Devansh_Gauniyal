namespace QuotesApi.Performance;

public sealed record AuthorQuoteSummary(int AuthorId, string Name, string Country, int QuoteCount);

// Deliberately slow: loads every author with one query, then - the way this happens by
// accident in real code - accesses each author's Quotes collection inside the loop via
// explicit loading instead of Include(). That is 1 query for the authors plus one more
// per author, i.e. 1 + N round trips to the database. Combined with the missing index on
// AuthorQuote.AuthorId (see PerformanceDbContext.ConfigureConventions), each of those N
// queries is also a full table scan.
public static class AuthorQuoteSummaryQuery
{
    public static List<AuthorQuoteSummary> Run(PerformanceDbContext context)
    {
        var authors = context.Authors.OrderBy(a => a.Id).ToList(); // query 1

        var summaries = new List<AuthorQuoteSummary>(authors.Count);
        foreach (var author in authors)
        {
            context.Entry(author).Collection(a => a.Quotes).Load(); // query 2..N+1
            summaries.Add(new AuthorQuoteSummary(author.Id, author.Name, author.Country, author.Quotes.Count));
        }

        return summaries;
    }
}
