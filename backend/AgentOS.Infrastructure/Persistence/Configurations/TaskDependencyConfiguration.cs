using AgentOS.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> builder)
    {
        builder.ToTable("TaskDependencies");
        builder.HasKey(dependency => new { dependency.TaskId, dependency.PrerequisiteTaskId });

        builder.HasOne<AutomationTask>()
            .WithMany()
            .HasForeignKey(dependency => dependency.TaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AutomationTask>()
            .WithMany()
            .HasForeignKey(dependency => dependency.PrerequisiteTaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(dependency => dependency.PrerequisiteTaskId);
    }
}
