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
    public DbSet<User> Users => Set<User>();

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

        var user = modelBuilder.Entity<User>();
        user.Property(value => value.Email)
            .HasMaxLength(User.MaximumEmailLength)
            .IsRequired();
        user.Property(value => value.PasswordHash)
            .IsRequired();
        user.HasIndex(value => value.Email)
            .IsUnique();
    }
}
