using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Quotes.Api.Data;

public sealed class QuotesDbContextFactory : IDesignTimeDbContextFactory<QuotesDbContext>
{
    public QuotesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlite("Data Source=quotes-task6.db")
            .Options;

        return new QuotesDbContext(options);
    }
}
