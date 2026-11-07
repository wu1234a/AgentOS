using AgentOS.Domain.GitHub;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class PullRequestReviewJobConfiguration : IEntityTypeConfiguration<PullRequestReviewJob>
{
    public void Configure(EntityTypeBuilder<PullRequestReviewJob> builder)
    {
        builder.ToTable("PullRequestReviewJobs");
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Repository).HasMaxLength(200).IsRequired();
        builder.Property(job => job.HeadSha).HasMaxLength(64).IsRequired();
        builder.Property(job => job.Action).HasMaxLength(40).IsRequired();
        builder.Property(job => job.DeliveryId).HasMaxLength(100).IsRequired();
        builder.Property(job => job.Status).HasMaxLength(20).IsRequired();
        builder.Property(job => job.LastError).HasMaxLength(1000);
        builder.Property(job => job.NextAttemptAtUtc).HasColumnType("datetime2(3)");
        builder.Property(job => job.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(job => job.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.HasIndex(job => new { job.Repository, job.PullRequestNumber, job.HeadSha }).IsUnique();
        builder.HasIndex(job => new { job.Status, job.NextAttemptAtUtc });
    }
}
