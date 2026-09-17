using System.Text.Json;
using Capstone.Invoicing.Domain;
using Capstone.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Capstone.Invoicing.Infrastructure.Persistence;

// Maps the Invoice aggregate exactly as Capstone.Invoicing.Domain already defines
// it - no domain type was changed to make this mapping possible except one:
// DueDate gained a private setter (see Invoice.cs's comment) so materialization
// restores the persisted value instead of silently re-deriving it from
// SubmittedAt+Terms on every read.
//
// Money is a `readonly record struct` with no identity of its own - exactly what
// EF Core's "complex property" mapping (stable since EF Core 8) exists for, as
// opposed to `OwnsOne`/`OwnsMany`, which model owned ENTITY types. Used for Terms
// and Approval below, both flat (no nested collection).
//
// Lines and MatchResult are NOT mapped as EF complex types, despite also being
// value objects with no identity - EF Core 10.0.12's native complex-type JSON
// mapping for COLLECTIONS (ComplexCollection().ToJson(), and a ComplexProperty
// containing one) hits a real bug in its query-shaping code the moment a row is
// read back: RelationalShapedQueryCompilingExpressionVisitor.CreateJsonShapers
// throws a bare NullReferenceException from
// EntityFrameworkMemberInfoExtensions.GetMemberType, reproduced live and
// consistently against a real SQL Server container (see
// tests/Capstone.Integration.Tests) regardless of the collection navigation's
// visibility or access mode - every variant tried (public/internal property,
// field vs property access mode, a dedicated shadow property) hit the identical
// failure, which points at the feature itself rather than this mapping's
// configuration. Both are stored instead as a single JSON-text column via a
// plain HasConversion, serialized/deserialized with System.Text.Json directly -
// a longer-established, simpler mechanism than EF's native complex-JSON
// collections, and unaffected by that bug because EF never treats the column as
// anything but a string.
internal sealed class InvoiceEntityTypeConfiguration : IEntityTypeConfiguration<Invoice>
{
    // MoneyJsonConverter is required: System.Text.Json's default reflection-based
    // deserialization cannot construct Money at all (a struct with no
    // parameterless constructor and no settable properties - confirmed live
    // that it silently produces Amount=0, Currency="" instead of using Money's
    // one public constructor, rather than throwing). See MoneyJsonConverter's
    // own comment. Applies automatically to every Money value nested anywhere
    // in Lines/MatchResult below, however deep.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new MoneyJsonConverter() },
    };

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

        // CorrectsInvoiceId (Day 30) - nullable, the same InvoiceId->Guid
        // conversion as the primary key above, just without ValueGeneratedNever
        // (this isn't a key). No foreign key constraint: the referenced invoice
        // lives in the same table, but SubmitInvoiceUseCase - not the database -
        // is what enforces "only a Rejected invoice may be corrected", since that
        // rule depends on the referenced row's STATUS at submission time, not
        // merely its existence.
        builder.Property(i => i.CorrectsInvoiceId)
            .HasConversion(
                id => id == null ? (Guid?)null : id.Value.Value,
                value => value == null ? (InvoiceId?)null : new InvoiceId(value.Value));

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

        // MatchResult (with its nested LineVariances, each carrying three Money
        // values) - a single JSON-text column via System.Text.Json, not EF's
        // native complex-JSON mapping. See this file's top comment for why.
        builder.Property(i => i.MatchResult)
            .HasConversion(
                matchResult => JsonSerializer.Serialize(matchResult, JsonOptions),
                json => JsonSerializer.Deserialize<MatchResult>(json, JsonOptions)!)
            .HasColumnName("MatchResult")
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        // Lines - same JSON-text-column approach as MatchResult above, backed by
        // the private `_lines` field (HasField tells EF where the deserialized
        // list lands; Lines itself has no setter).
        builder.Property(i => i.Lines)
            .HasField("_lines")
            .HasConversion(
                lines => JsonSerializer.Serialize(lines, JsonOptions),
                json => JsonSerializer.Deserialize<List<InvoiceLineItem>>(json, JsonOptions)!)
            .HasColumnName("Lines")
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Ignore(i => i.Total);

        // Day 31: measured live (bombardier, 50,000-row local dataset) that
        // GET /v1/invoices - InvoiceEfRepository.ListAsync's
        // OrderBy(SubmittedAt).Skip().Take() - was the hottest path in the API by
        // a wide margin (p99 508ms, versus 5.76ms for a by-id lookup and 36ms for
        // a submit) precisely because no index supported that ORDER BY: SQL
        // Server had to sort the entire table on every single page request,
        // however small the page. This index lets it read rows already in
        // SubmittedAt order and apply Skip/Take without a full sort - see
        // submission-day-31-task-1.md for the before/after numbers.
        builder.HasIndex(i => i.SubmittedAt).HasDatabaseName("IX_Invoices_SubmittedAt");

        // Day 31: the same gap on the other unindexed filter this table takes -
        // InvoiceEfRepository.FindSubmittedAsync's WHERE Status = 'Submitted',
        // read every time DeemedApprovalSweepBackgroundService (or its manual
        // trigger endpoint) ticks. Not the endpoint actually benchmarked today,
        // but the identical unindexed-scan shape, on the same table, discovered
        // while looking at this one - fixed alongside it rather than left for a
        // separate day now that the cause is already understood.
        builder.HasIndex(i => i.Status).HasDatabaseName("IX_Invoices_Status");
    }
}
