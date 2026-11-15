using AgentOS.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class ProjectMilestoneConfiguration : IEntityTypeConfiguration<ProjectMilestone>
{
    public void Configure(EntityTypeBuilder<ProjectMilestone> builder)
    {
        builder.ToTable("ProjectMilestones");
        builder.HasKey(milestone => milestone.Id);
        builder.Property(milestone => milestone.Key).HasMaxLength(40).IsRequired();
        builder.Property(milestone => milestone.Title).HasMaxLength(200).IsRequired();
        builder.Property(milestone => milestone.Goal).HasMaxLength(2000).IsRequired();
        builder.Property(milestone => milestone.ExitCriteria).HasMaxLength(4000).IsRequired();
        builder.Property(milestone => milestone.Status).HasMaxLength(20).IsRequired();
        builder.Property(milestone => milestone.VerifiedBy).HasMaxLength(120);
        builder.Property(milestone => milestone.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.HasIndex(milestone => new { milestone.PlanId, milestone.Key }).IsUnique();
        builder.HasIndex(milestone => new { milestone.ProjectId, milestone.Status });
    }
}
