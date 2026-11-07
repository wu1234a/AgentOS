using AgentOS.Domain.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class ReviewRunConfiguration : IEntityTypeConfiguration<ReviewRun>
{
    public void Configure(EntityTypeBuilder<ReviewRun> builder)
    {
        builder.ToTable("ReviewRuns");
        builder.HasKey(review => review.Id);

        builder.Property(review => review.BaseRevision).HasMaxLength(64);
        builder.Property(review => review.HeadRevision).HasMaxLength(64);
        builder.Property(review => review.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(review => review.Conclusion).HasConversion<string>().HasMaxLength(20);
        builder.Property(review => review.Summary).HasMaxLength(4000);
        builder.Property(review => review.ErrorCode).HasMaxLength(100);
        builder.Property(review => review.ErrorMessage).HasMaxLength(2000);
        builder.Property(review => review.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(review => review.StartedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(review => review.CompletedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(review => review.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(review => review.RowVersion).IsRowVersion();

        builder.HasOne<Domain.CodingRuns.CodingRun>()
            .WithMany()
            .HasForeignKey(review => review.CodingRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(review => review.CodingRunId).IsUnique();
    }
}

public sealed class ReviewFindingConfiguration : IEntityTypeConfiguration<ReviewFinding>
{
    public void Configure(EntityTypeBuilder<ReviewFinding> builder)
    {
        builder.ToTable("ReviewFindings");
        builder.HasKey(finding => finding.Id);

        builder.Property(finding => finding.Severity).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(finding => finding.FilePath).HasMaxLength(500).IsRequired();
        builder.Property(finding => finding.Reason).HasMaxLength(2000).IsRequired();
        builder.Property(finding => finding.Suggestion).HasMaxLength(2000).IsRequired();

        builder.HasOne<ReviewRun>()
            .WithMany()
            .HasForeignKey(finding => finding.ReviewRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(finding => new { finding.ReviewRunId, finding.SortOrder }).IsUnique();
    }
}
