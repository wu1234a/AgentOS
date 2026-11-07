using AgentOS.Domain.Agents;
using AgentOS.Domain.Events;
using AgentOS.Domain.ModelProfiles;
using AgentOS.Domain.Projects;
using AgentOS.Domain.Rules;
using AgentOS.Domain.Skills;
using AgentOS.Domain.Tasks;
using AgentOS.Domain.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class AutomationTaskConfiguration : IEntityTypeConfiguration<AutomationTask>
{
    public void Configure(EntityTypeBuilder<AutomationTask> builder)
    {
        builder.ToTable(
            "Tasks",
            table => table.HasCheckConstraint(
                "CK_Tasks_InputJson_IsJson",
                "ISJSON([InputJson]) = 1"));

        builder.HasKey(task => task.Id);

        builder.Property(task => task.TaskCode).HasMaxLength(40).IsRequired();
        builder.Property(task => task.LogicalTaskKey).HasMaxLength(LogicalTaskKey.MaxLength).IsRequired();
        builder.Property(task => task.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(task => task.Priority).HasMaxLength(16).IsRequired();
        builder.Property(task => task.Title).HasMaxLength(200).IsRequired();
        builder.Property(task => task.Description).HasMaxLength(2000);
        builder.Property(task => task.InputJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(task => task.NextRunAtUtc).HasColumnType("datetime2(3)");
        builder.Property(task => task.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(task => task.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(task => task.LeaseTokenHash).HasMaxLength(64);
        builder.Property(task => task.LeaseExpiresAtUtc).HasColumnType("datetime2(3)");
        builder.Property(task => task.LastHeartbeatAtUtc).HasColumnType("datetime2(3)");
        builder.Property(task => task.StartedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(task => task.CompletedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(task => task.CancelRequestedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(task => task.ErrorCode).HasMaxLength(100);
        builder.Property(task => task.ErrorMessage).HasMaxLength(2000);
        builder.Property(task => task.RowVersion).IsRowVersion();

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(task => task.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(task => task.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AutomationRule>()
            .WithMany()
            .HasForeignKey(task => task.RuleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(task => task.AgentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Skill>()
            .WithMany()
            .HasForeignKey(task => task.SkillId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ModelProfile>()
            .WithMany()
            .HasForeignKey(task => task.ModelProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Worker>()
            .WithMany()
            .HasForeignKey(task => task.LeaseOwnerWorkerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(task => task.PhaseKey).HasMaxLength(64);
        builder.HasIndex(task => task.TaskCode)
            .IsUnique()
            .HasDatabaseName("UX_Tasks_TaskCode");

        // The logical idempotency key: one task per event + rule revision + dispatch key.
        builder.HasIndex(task => task.LogicalTaskKey)
            .IsUnique()
            .HasDatabaseName("UX_Tasks_LogicalTaskKey");

        builder.HasIndex(task => new { task.Status, task.Priority, task.NextRunAtUtc })
            .HasDatabaseName("IX_Tasks_Status_Priority_NextRunAtUtc");

        builder.HasIndex(task => task.EventId);
        builder.HasIndex(task => task.RuleId);
        builder.HasIndex(task => task.LeaseExpiresAtUtc)
            .HasDatabaseName("IX_Tasks_LeaseExpiresAtUtc");
    }
}
