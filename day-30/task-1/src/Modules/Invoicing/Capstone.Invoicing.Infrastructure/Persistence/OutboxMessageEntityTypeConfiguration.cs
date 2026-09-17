using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Capstone.Invoicing.Infrastructure.Persistence;

internal sealed class OutboxMessageEntityTypeConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages", schema: "invoicing");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.EventType).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(m => m.OccurredAt).IsRequired();
        builder.Property(m => m.PublishedAt);

        // The one query OutboxRelayBackgroundService actually runs - unpublished
        // rows, oldest first.
        builder.HasIndex(m => m.PublishedAt);
    }
}
