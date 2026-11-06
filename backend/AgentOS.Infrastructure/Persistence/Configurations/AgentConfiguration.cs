using AgentOS.Domain.Agents;
using AgentOS.Domain.ModelProfiles;
using AgentOS.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable("Agents");

        builder.HasKey(agent => agent.Id);

        builder.Property(agent => agent.Code).HasMaxLength(80).IsRequired();
        builder.Property(agent => agent.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(agent => agent.Role).HasMaxLength(40).IsRequired();
        builder.Property(agent => agent.Description).HasMaxLength(1000);
        builder.Property(agent => agent.SystemPromptPath).HasMaxLength(500);
        builder.Property(agent => agent.PermissionProfile).HasMaxLength(40).IsRequired();
        builder.Property(agent => agent.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(agent => agent.UpdatedAtUtc).HasColumnType("datetime2(3)");

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(agent => agent.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ModelProfile>()
            .WithMany()
            .HasForeignKey(agent => agent.DefaultModelProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        // Codes are unique per project; global agents (ProjectId IS NULL) have their own scope.
        builder.HasIndex(agent => new { agent.ProjectId, agent.Code })
            .IsUnique()
            .HasFilter("[ProjectId] IS NOT NULL")
            .HasDatabaseName("UX_Agents_Project_Code");

        builder.HasIndex(agent => agent.Code)
            .IsUnique()
            .HasFilter("[ProjectId] IS NULL")
            .HasDatabaseName("UX_Agents_Global_Code");
    }
}
