using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class QuotesDbContext : DbContext
{
    public QuotesDbContext(DbContextOptions<QuotesDbContext> options)
        : base(options)
    {
    }

    public DbSet<Quote> Quotes => Set<Quote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var quote = modelBuilder.Entity<Quote>();
        quote.Property(value => value.Author)
            .HasMaxLength(Quote.MaximumAuthorLength)
            .IsRequired();
        quote.Property(value => value.Text)
            .HasMaxLength(Quote.MaximumTextLength)
            .IsRequired();
        quote.HasQueryFilter(value => !value.IsDeleted);
    }
}
