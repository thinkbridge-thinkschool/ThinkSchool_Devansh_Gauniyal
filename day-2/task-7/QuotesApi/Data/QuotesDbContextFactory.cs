using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QuotesApi.Data;

public sealed class QuotesDbContextFactory : IDesignTimeDbContextFactory<QuotesDbContext>
{
    public QuotesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlite("Data Source=quotes.db")
            .Options;

        return new QuotesDbContext(options);
    }
}
