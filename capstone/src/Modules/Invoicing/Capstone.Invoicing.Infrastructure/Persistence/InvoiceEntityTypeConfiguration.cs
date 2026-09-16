using Capstone.Invoicing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Capstone.Invoicing.Infrastructure.Persistence;

// Maps the Invoice aggregate exactly as Capstone.Invoicing.Domain already defines
// it. Only one domain type changed to make this mapping possible: DueDate gained a
// private setter (see Invoice.cs's comment) so materialization restores the
// persisted value instead of silently re-deriving it from SubmittedAt+Terms on
// every read - everything else here maps the aggregate as it already stood.
//
// Money is a `readonly record struct` with no identity of its own - exactly what
// EF Core's "complex property" mapping (stable since EF Core 8) exists for, as
// opposed to `OwnsOne`/`OwnsMany`, which model owned ENTITY types. Used
// consistently below, whether Money sits directly on the aggregate (there is none
// here - see PurchaseOrderEntityTypeConfiguration for that case) or nested inside a
// JSON-mapped owned collection (Lines, MatchResult's LineVariances).
internal sealed class InvoiceEntityTypeConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices", schema: "invoicing");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasConversion(id => id.Value, value => new InvoiceId(value))
            .ValueGeneratedNever();

        builder.Property(i => i.SupplierId).IsRequired();
        builder.Property(i => i.BuyerId).IsRequired();

        builder.Property(i => i.PurchaseOrderId)
            .HasConversion(reference => reference.Value, value => new PurchaseOrderReference(value))
            .IsRequired();

        builder.Property(i => i.InvoiceNumber).HasMaxLength(64).IsRequired();
        builder.Property(i => i.Currency).HasMaxLength(3).IsRequired();
        builder.Property(i => i.SubmittedAt).IsRequired();
        builder.Property(i => i.DueDate).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.DisputeReason).HasMaxLength(1000);

        // Terms: two positive ints, always present at submission - a flat,
        // column-split complex property (Terms_NetDays, Terms_ReviewWindowDays),
        // no JSON needed for something this simple.
        builder.ComplexProperty(i => i.Terms, terms =>
        {
            terms.Property(t => t.NetDays).HasColumnName("Terms_NetDays").IsRequired();
            terms.Property(t => t.ReviewWindowDays).HasColumnName("Terms_ReviewWindowDays").IsRequired();
        });

        // Approval: absent until the invoice is Approved - nullable column-split
        // complex property rather than JSON, since it's flat (no nested Money or
        // collection to decompose further).
        builder.ComplexProperty(i => i.Approval, approval =>
        {
            approval.IsRequired(false);
            approval.Property(a => a.ApprovedAt).HasColumnName("Approval_ApprovedAt");
            approval.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20).HasColumnName("Approval_Kind");
            approval.Property(a => a.ApprovedBy).HasColumnName("Approval_ApprovedBy");
        });

        // MatchResult (plus its nested LineVariances, each carrying three Money
        // values) maps to one JSON column - it is only ever read and written
        // whole, alongside its owning invoice, never queried into directly, which
        // is exactly the case EF Core's JSON-column complex-type mapping is for.
        // Money, InvoiceLineItem, MatchResult and LineVariance are all modelled as
        // EF "complex types" (ComplexProperty/ComplexCollection), not owned entity
        // types (OwnsOne/OwnsMany) - correct here since none of them has identity
        // of its own; EF Core's complex-type collections require a JSON column,
        // which is exactly the storage shape these value objects need anyway.
        builder.ComplexProperty(i => i.MatchResult, matchResult =>
        {
            matchResult.ToJson();
            matchResult.ComplexCollection(m => m.LineVariances, variances =>
            {
                variances.ComplexProperty(v => v.Invoiced, money =>
                {
                    money.Property(m => m.Amount);
                    money.Property(m => m.Currency);
                });
                variances.ComplexProperty(v => v.PurchaseOrderLineValue, money =>
                {
                    money.Property(m => m.Amount);
                    money.Property(m => m.Currency);
                });
                variances.ComplexProperty(v => v.Variance, money =>
                {
                    money.Property(m => m.Amount);
                    money.Property(m => m.Currency);
                });
            });
        });

        // Lines: a complex-type collection backed by the private `_lines` field -
        // EF Core's default backing-field convention matches `_lines` to the
        // get-only `Lines` property automatically, so the aggregate's own
        // encapsulation (no public setter, no way to hand the collection to the
        // caller for mutation) needed no changes for this to work. Also a JSON
        // column, for the same reason as MatchResult above.
        builder.ComplexCollection(i => i.Lines, lines =>
        {
            lines.HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
            lines.ToJson();
            lines.Ignore(l => l.LineAmount);
            lines.ComplexProperty(l => l.UnitPrice, money =>
            {
                money.Property(m => m.Amount);
                money.Property(m => m.Currency);
            });
        });

        builder.Ignore(i => i.Total);
    }
}
