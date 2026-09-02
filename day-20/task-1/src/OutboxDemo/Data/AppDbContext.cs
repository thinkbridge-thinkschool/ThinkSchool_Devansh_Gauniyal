using Microsoft.EntityFrameworkCore;
using OutboxDemo.Domain;

namespace OutboxDemo.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.HasKey(o => o.Id);
            order.HasMany(o => o.OutboxMessages)
                .WithOne(m => m.Order)
                .HasForeignKey(m => m.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OutboxMessage>(message =>
        {
            message.HasKey(m => m.Id);
            message.Property(m => m.Type).IsRequired();
            message.Property(m => m.Payload).IsRequired();
        });

        modelBuilder.Entity<ProcessedMessage>(processed =>
        {
            processed.HasKey(p => p.OutboxMessageId);
        });
    }
}
