using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Quotes.Api.Data;

public sealed class QuotesDbContextFactory : IDesignTimeDbContextFactory<QuotesDbContext>
{
    public QuotesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlServer()
            .Options;

        return new QuotesDbContext(options);
    }
}
