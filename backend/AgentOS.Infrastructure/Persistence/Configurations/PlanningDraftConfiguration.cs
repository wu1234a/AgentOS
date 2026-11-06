using AgentOS.Domain.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class PlanningDraftConfiguration : IEntityTypeConfiguration<PlanningDraft>
{
    public void Configure(EntityTypeBuilder<PlanningDraft> builder)
    {
        builder.ToTable("PlanningDrafts");

        builder.HasKey(draft => draft.Id);

        builder.Property(draft => draft.Title).HasMaxLength(200).IsRequired();
        builder.Property(draft => draft.Goal).HasMaxLength(4000).IsRequired();
        builder.Property(draft => draft.Context).HasMaxLength(8000);
        builder.Property(draft => draft.ModelProfileCode).HasMaxLength(80);
        builder.Property(draft => draft.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(draft => draft.ContentMarkdown).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(draft => draft.ErrorMessage).HasMaxLength(2000);
        builder.Property(draft => draft.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(draft => draft.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(draft => draft.DecidedAtUtc).HasColumnType("datetime2(3)");

        builder.HasOne<Domain.Projects.Project>()
            .WithMany()
            .HasForeignKey(draft => draft.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(draft => new { draft.Status, draft.CreatedAtUtc });
    }
}
