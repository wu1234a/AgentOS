using AgentOS.Domain.Artifacts;
using AgentOS.Domain.Outbox;
using AgentOS.Domain.Tasks;
using AgentOS.Domain.Usage;
using AgentOS.Domain.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class TaskLogConfiguration : IEntityTypeConfiguration<TaskLog>
{
    public void Configure(EntityTypeBuilder<TaskLog> builder)
    {
        builder.ToTable("TaskLogs");

        builder.HasKey(log => log.Id);

        builder.Property(log => log.Level).HasMaxLength(16).IsRequired();
        builder.Property(log => log.Component).HasMaxLength(64).IsRequired();
        builder.Property(log => log.Message).HasMaxLength(4000).IsRequired();
        builder.Property(log => log.PropertiesJson).HasColumnType("nvarchar(max)");
        builder.Property(log => log.CreatedAtUtc).HasColumnType("datetime2(3)");

        builder.HasOne<AutomationTask>()
            .WithMany()
            .HasForeignKey(log => log.TaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TaskAttempt>()
            .WithMany()
            .HasForeignKey(log => log.AttemptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Worker>()
            .WithMany()
            .HasForeignKey(log => log.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(log => new { log.AttemptId, log.Sequence })
            .IsUnique()
            .HasDatabaseName("UX_TaskLogs_Attempt_Sequence");

        builder.HasIndex(log => new { log.TaskId, log.CreatedAtUtc });
        builder.HasIndex(log => new { log.TaskId, log.Level });
    }
}

public sealed class ArtifactConfiguration : IEntityTypeConfiguration<Artifact>
{
    public void Configure(EntityTypeBuilder<Artifact> builder)
    {
        builder.ToTable("Artifacts");

        builder.HasKey(artifact => artifact.Id);

        builder.Property(artifact => artifact.Name).HasMaxLength(200).IsRequired();
        builder.Property(artifact => artifact.RelativePath).HasMaxLength(500).IsRequired();
        builder.Property(artifact => artifact.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(artifact => artifact.MediaType).HasMaxLength(100).IsRequired();
        builder.Property(artifact => artifact.CreatedAtUtc).HasColumnType("datetime2(3)");

        builder.HasOne<AutomationTask>()
            .WithMany()
            .HasForeignKey(artifact => artifact.TaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TaskAttempt>()
            .WithMany()
            .HasForeignKey(artifact => artifact.AttemptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(artifact => artifact.RelativePath)
            .IsUnique()
            .HasDatabaseName("UX_Artifacts_RelativePath");

        builder.HasIndex(artifact => new { artifact.TaskId, artifact.CreatedAtUtc });
        builder.HasIndex(artifact => artifact.AttemptId);
    }
}

public sealed class UsageRecordConfiguration : IEntityTypeConfiguration<UsageRecord>
{
    public void Configure(EntityTypeBuilder<UsageRecord> builder)
    {
        builder.ToTable("UsageRecords");

        builder.HasKey(usage => usage.Id);

        builder.Property(usage => usage.Provider).HasMaxLength(50).IsRequired();
        builder.Property(usage => usage.Model).HasMaxLength(100);
        builder.Property(usage => usage.Source).HasMaxLength(20).IsRequired();
        builder.Property(usage => usage.CreatedAtUtc).HasColumnType("datetime2(3)");

        builder.HasOne<AutomationTask>()
            .WithMany()
            .HasForeignKey(usage => usage.TaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TaskAttempt>()
            .WithMany()
            .HasForeignKey(usage => usage.AttemptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(usage => usage.TaskId);
    }
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Type).HasMaxLength(100).IsRequired();
        builder.Property(message => message.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(message => message.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(1000);
        builder.Property(message => message.AvailableAtUtc).HasColumnType("datetime2(3)");
        builder.Property(message => message.ProcessedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(message => message.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(message => message.UpdatedAtUtc).HasColumnType("datetime2(3)");

        builder.HasIndex(message => new { message.Status, message.AvailableAtUtc });
    }
}
