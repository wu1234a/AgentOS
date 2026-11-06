using AgentOS.Domain.Tasks;
using AgentOS.Domain.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class TaskAttemptConfiguration : IEntityTypeConfiguration<TaskAttempt>
{
    public void Configure(EntityTypeBuilder<TaskAttempt> builder)
    {
        builder.ToTable("TaskAttempts");

        builder.HasKey(attempt => attempt.Id);

        builder.Property(attempt => attempt.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(attempt => attempt.ExternalSessionId).HasMaxLength(200);
        builder.Property(attempt => attempt.ErrorCode).HasMaxLength(100);
        builder.Property(attempt => attempt.ErrorMessage).HasMaxLength(2000);
        builder.Property(attempt => attempt.StartedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(attempt => attempt.FinishedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(attempt => attempt.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(attempt => attempt.UpdatedAtUtc).HasColumnType("datetime2(3)");

        builder.HasOne<AutomationTask>()
            .WithMany()
            .HasForeignKey(attempt => attempt.TaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Worker>()
            .WithMany()
            .HasForeignKey(attempt => attempt.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);

        // A retry creates a new attempt; the same task can never reuse an attempt number.
        builder.HasIndex(attempt => new { attempt.TaskId, attempt.AttemptNumber })
            .IsUnique()
            .HasDatabaseName("UX_TaskAttempts_Task_Attempt");

        builder.HasIndex(attempt => attempt.WorkerId);
    }
}
