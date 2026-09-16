using System.Text.Json;
using Capstone.Procurement.Domain;
using Capstone.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Capstone.Procurement.Infrastructure.Persistence;

// Maps the PurchaseOrder aggregate exactly as Capstone.Procurement.Domain already
// defines it - no domain type needed to change for this one (unlike Invoice; see
// Capstone.Invoicing.Infrastructure.Persistence's equivalent comment). Reserved and
// Consumed already have private setters and are computed once, in the constructor
// and the Reserve/Release/Consume methods, never re-derived on read.
internal sealed class PurchaseOrderEntityTypeConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    // See Capstone.Invoicing.Infrastructure.Persistence.InvoiceEntityTypeConfiguration's
    // identical field and MoneyJsonConverter's own comment for why this is
    // required.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new MoneyJsonConverter() },
    };

    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("PurchaseOrders", schema: "procurement");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new PurchaseOrderId(value))
            .ValueGeneratedNever();

        builder.Property(p => p.VendorId).IsRequired();
        builder.Property(p => p.BuyerId).IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Reserved/Consumed: Money (a value object with no identity) mapped as a
        // complex property, not an owned entity type - see
        // Capstone.Invoicing.Infrastructure.Persistence.InvoiceEntityTypeConfiguration's
        // comment on the same choice.
        builder.ComplexProperty(p => p.Reserved, reserved =>
        {
            reserved.Property(m => m.Amount).HasColumnName("Reserved_Amount").HasColumnType("decimal(18,2)");
            reserved.Property(m => m.Currency).HasColumnName("Reserved_Currency").HasMaxLength(3);
        });
        builder.ComplexProperty(p => p.Consumed, consumed =>
        {
            consumed.Property(m => m.Amount).HasColumnName("Consumed_Amount").HasColumnType("decimal(18,2)");
            consumed.Property(m => m.Currency).HasColumnName("Consumed_Currency").HasMaxLength(3);
        });

        // Lines - a single JSON-text column via System.Text.Json, not EF's
        // native complex-JSON collection mapping - see
        // Capstone.Invoicing.Infrastructure.Persistence.InvoiceEntityTypeConfiguration's
        // top comment for why (a real bug in that feature's query-shaping code,
        // reproduced live against a real SQL Server integration test).
        builder.Property(p => p.Lines)
            .HasField("_lines")
            .HasConversion(
                lines => JsonSerializer.Serialize(lines, JsonOptions),
                json => JsonSerializer.Deserialize<List<PurchaseOrderLine>>(json, JsonOptions)!)
            .HasColumnName("Lines")
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Ignore(p => p.Total);
        builder.Ignore(p => p.Available);
    }
}
