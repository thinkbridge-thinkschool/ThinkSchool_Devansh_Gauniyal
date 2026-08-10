using Microsoft.EntityFrameworkCore;
using OrderApi.Models;

namespace OrderApi.Data;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var order = modelBuilder.Entity<Order>();
        order.HasKey(x => x.Id);
        order.Property(x => x.CustomerName).HasMaxLength(100).IsRequired();
        order.Property(x => x.CustomerEmail).HasMaxLength(254).IsRequired();
        order.Property(x => x.ProductCode).HasMaxLength(40).IsRequired();
        order.Property(x => x.UnitPrice).HasPrecision(18, 2);
        order.Property(x => x.DiscountAmount).HasPrecision(18, 2);
        order.Property(x => x.TotalAmount).HasPrecision(18, 2);
        order.Property(x => x.Status).HasMaxLength(30).IsRequired();
    }
}
