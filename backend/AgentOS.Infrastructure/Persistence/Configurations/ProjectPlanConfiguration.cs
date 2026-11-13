using AgentOS.Domain.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class ProjectPlanConfiguration : IEntityTypeConfiguration<ProjectPlan>
{
    public void Configure(EntityTypeBuilder<ProjectPlan> builder)
    {
        builder.ToTable("ProjectPlans");
        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.Title).HasMaxLength(200).IsRequired();
        builder.Property(plan => plan.Goal).HasMaxLength(8000).IsRequired();
        builder.Property(plan => plan.Constraints).HasMaxLength(8000);
        builder.Property(plan => plan.DeliveryJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(plan => plan.ModelProfileCode).HasMaxLength(80).IsRequired();
        builder.Property(plan => plan.Status).HasMaxLength(30).IsRequired();
        builder.Property(plan => plan.ProposalsJson).HasColumnType("nvarchar(max)");
        builder.Property(plan => plan.CritiquesJson).HasColumnType("nvarchar(max)");
        builder.Property(plan => plan.FinalPlanJson).HasColumnType("nvarchar(max)");
        builder.Property(plan => plan.Error).HasMaxLength(1000);
        builder.Property(plan => plan.Feedback).HasMaxLength(8000);
        builder.Property(plan => plan.Revision).HasDefaultValue(1);
        builder.Property(plan => plan.TokenBudget).HasDefaultValue(60_000);
        builder.Property(plan => plan.EstimatedCost).HasPrecision(18, 6);
        builder.Property(plan => plan.PreviousFinalPlanJson).HasColumnType("nvarchar(max)");
        builder.Property(plan => plan.RequiresReleaseApproval).HasDefaultValue(true);
        builder.Property(plan => plan.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(plan => plan.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.HasIndex(plan => new { plan.ProjectId, plan.Status, plan.CreatedAtUtc });
    }
}

public sealed class ProjectPlanContributionConfiguration : IEntityTypeConfiguration<ProjectPlanContribution>
{
    public void Configure(EntityTypeBuilder<ProjectPlanContribution> builder)
    {
        builder.ToTable("ProjectPlanContributions");
        builder.HasKey(contribution => contribution.Id);
        builder.Property(contribution => contribution.Role).HasMaxLength(40).IsRequired();
        builder.Property(contribution => contribution.ModelProfileCode).HasMaxLength(80).IsRequired();
        builder.Property(contribution => contribution.Content).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(contribution => contribution.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.HasIndex(contribution => new { contribution.PlanId, contribution.Round, contribution.Role });
    }
}
