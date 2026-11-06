using AgentOS.Domain.Agents;
using AgentOS.Domain.ModelProfiles;
using AgentOS.Domain.Projects;
using AgentOS.Domain.Rules;
using AgentOS.Domain.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class AutomationRuleConfiguration : IEntityTypeConfiguration<AutomationRule>
{
    public void Configure(EntityTypeBuilder<AutomationRule> builder)
    {
        builder.ToTable(
            "AutomationRules",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_AutomationRules_MatchJson_IsJson",
                    "ISJSON([MatchJson]) = 1");
                table.HasCheckConstraint(
                    "CK_AutomationRules_DispatchJson_IsJson",
                    "ISJSON([DispatchJson]) = 1");
            });

        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.Name).HasMaxLength(120).IsRequired();
        builder.Property(rule => rule.SourcePattern).HasMaxLength(64).IsRequired();
        builder.Property(rule => rule.EventTypePattern).HasMaxLength(120).IsRequired();
        builder.Property(rule => rule.MatchJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(rule => rule.DispatchJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(rule => rule.NotificationPolicy).HasMaxLength(60).IsRequired();
        builder.Property(rule => rule.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(rule => rule.UpdatedAtUtc).HasColumnType("datetime2(3)");

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(rule => rule.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(rule => rule.AgentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Skill>()
            .WithMany()
            .HasForeignKey(rule => rule.SkillId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ModelProfile>()
            .WithMany()
            .HasForeignKey(rule => rule.ModelProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(rule => rule.IsEnabled);
        builder.HasIndex(rule => new { rule.ProjectId, rule.IsEnabled });
    }
}
