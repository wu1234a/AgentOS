using AgentOS.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable(
            "Events",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_Events_PayloadJson_IsJson",
                    "ISJSON([PayloadJson]) = 1");
                table.HasCheckConstraint(
                    "CK_Events_PayloadHash_LowerHex64",
                    "[PayloadHash] IS NULL OR (LEN([PayloadHash]) = 64 AND [PayloadHash] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%')");
            });

        builder.HasKey(evt => evt.Id);

        builder.Property(evt => evt.Source).HasMaxLength(64).IsRequired();
        builder.Property(evt => evt.EventType).HasMaxLength(120).IsRequired();
        builder.Property(evt => evt.SchemaVersion).HasMaxLength(16).IsRequired();
        builder.Property(evt => evt.ExternalEventId).HasMaxLength(200);
        builder.Property(evt => evt.DedupeKey).HasMaxLength(400).IsRequired();
        builder.Property(evt => evt.Summary).HasMaxLength(500).IsRequired();
        builder.Property(evt => evt.ResourceUrl).HasMaxLength(2048);
        builder.Property(evt => evt.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        // Nullable on purpose: rows written before AOS-003 are hashed lazily on first duplicate.
        builder.Property(evt => evt.PayloadHash).HasMaxLength(64);
        builder.Property(evt => evt.SubjectType).HasMaxLength(64);
        builder.Property(evt => evt.SubjectId).HasMaxLength(200);
        builder.Property(evt => evt.Priority).HasMaxLength(16).IsRequired();
        builder.Property(evt => evt.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(evt => evt.ProcessedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(evt => evt.OccurredAtUtc).HasColumnType("datetime2(3)");
        builder.Property(evt => evt.ReceivedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(evt => evt.CorrelationId).HasMaxLength(100);

        builder.HasOne(evt => evt.Project)
            .WithMany()
            .HasForeignKey(evt => evt.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        // Final line of defence for event idempotency: concurrent submissions of the same
        // Source + DedupeKey can only ever produce one row.
        builder.HasIndex(evt => new { evt.Source, evt.DedupeKey })
            .IsUnique()
            .HasDatabaseName("UX_Events_Source_DedupeKey");

        builder.HasIndex(evt => evt.ReceivedAtUtc);
        builder.HasIndex(evt => new { evt.ProjectId, evt.EventType, evt.ReceivedAtUtc });
        builder.HasIndex(evt => new { evt.Source, evt.ReceivedAtUtc });
    }
}
