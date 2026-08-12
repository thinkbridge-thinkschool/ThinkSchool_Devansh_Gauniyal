using Microsoft.EntityFrameworkCore;
using Quotes.Api.Models;

namespace Quotes.Api.Data;

public sealed class QuotesDbContext(DbContextOptions<QuotesDbContext> options)
    : DbContext(options)
{
    public DbSet<Quote> Quotes => Set<Quote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var quote = modelBuilder.Entity<Quote>();
        quote.HasKey(item => item.Id);
        quote.Property(item => item.OwnerId).IsRequired().HasMaxLength(100);
        quote.Property(item => item.Text).IsRequired().HasMaxLength(280);
        quote.Property(item => item.CreatedAtUtc).IsRequired();
    }
}
