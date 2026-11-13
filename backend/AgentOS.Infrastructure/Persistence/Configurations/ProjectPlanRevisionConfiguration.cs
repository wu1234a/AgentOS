using AgentOS.Domain.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class ProjectPlanRevisionConfiguration : IEntityTypeConfiguration<ProjectPlanRevision>
{
    public void Configure(EntityTypeBuilder<ProjectPlanRevision> builder)
    {
        builder.ToTable("ProjectPlanRevisions");
        builder.HasKey(revision => revision.Id);
        builder.Property(revision => revision.FinalPlanJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(revision => revision.Feedback).HasMaxLength(8000);
        builder.Property(revision => revision.ModelProfileCode).HasMaxLength(80).IsRequired();
        builder.Property(revision => revision.EstimatedCost).HasPrecision(18, 6);
        builder.Property(revision => revision.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.HasIndex(revision => new { revision.PlanId, revision.Revision }).IsUnique();
    }
}
