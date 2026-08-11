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
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

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

        var refreshToken = modelBuilder.Entity<RefreshToken>();
        refreshToken.Property(value => value.Token)
            .HasMaxLength(RefreshToken.TokenHashLength)
            .IsRequired();
        refreshToken.Property(value => value.ReplacedByToken)
            .HasMaxLength(RefreshToken.TokenHashLength);
        refreshToken.Property(value => value.RevokedAt)
            .IsConcurrencyToken();
        refreshToken.Property(value => value.ReplacedByToken)
            .IsConcurrencyToken();
        refreshToken.HasIndex(value => value.Token)
            .IsUnique();
        refreshToken.HasIndex(value => value.UserId);
        refreshToken.HasOne(value => value.User)
            .WithMany()
            .HasForeignKey(value => value.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
