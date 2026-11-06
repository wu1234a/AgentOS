using AgentOS.Domain.Tasks;
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
        builder.Property(log => log.Message).HasMaxLength(4000).IsRequired();
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

        // Batch retries re-send the same sequence numbers; the unique key keeps them idempotent.
        builder.HasIndex(log => new { log.AttemptId, log.Sequence })
            .IsUnique()
            .HasDatabaseName("UX_TaskLogs_Attempt_Sequence");

        builder.HasIndex(log => new { log.TaskId, log.CreatedAtUtc });
    }
}
