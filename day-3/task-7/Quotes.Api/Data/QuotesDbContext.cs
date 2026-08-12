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
        quote.Property(item => item.Id).UseIdentityColumn();
        quote.Property(item => item.OwnerId).HasMaxLength(100).IsRequired();
        quote.Property(item => item.Text).HasMaxLength(280).IsRequired();
        quote.Property(item => item.CreatedAtUtc).HasPrecision(7).IsRequired();
    }
}
