using AgentOS.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events", table => table.HasCheckConstraint(
            "CK_Events_PayloadJson_IsJson",
            "ISJSON([PayloadJson]) = 1"));

        builder.HasKey(evt => evt.Id);

        builder.Property(evt => evt.Source).HasMaxLength(64).IsRequired();
        builder.Property(evt => evt.EventType).HasMaxLength(120).IsRequired();
        builder.Property(evt => evt.SchemaVersion).HasMaxLength(16).IsRequired();
        builder.Property(evt => evt.ExternalEventId).HasMaxLength(200);
        builder.Property(evt => evt.DedupeKey).HasMaxLength(400).IsRequired();
        builder.Property(evt => evt.Summary).HasMaxLength(500).IsRequired();
        builder.Property(evt => evt.ResourceUrl).HasMaxLength(2048);
        builder.Property(evt => evt.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
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

        builder.HasIndex(evt => evt.ReceivedAtUtc);
        builder.HasIndex(evt => new { evt.ProjectId, evt.EventType, evt.ReceivedAtUtc });
        builder.HasIndex(evt => new { evt.Source, evt.ReceivedAtUtc });
    }
}
