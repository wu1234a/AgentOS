using AgentOS.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class ScheduledJobConfiguration : IEntityTypeConfiguration<ScheduledJob>
{
    public void Configure(EntityTypeBuilder<ScheduledJob> builder)
    {
        builder.ToTable("ScheduledJobs");
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Name).HasMaxLength(120).IsRequired();
        builder.Property(job => job.EventType).HasMaxLength(100).IsRequired();
        builder.Property(job => job.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(job => job.FirstRunAtUtc).HasColumnType("datetime2(3)");
        builder.Property(job => job.NextRunAtUtc).HasColumnType("datetime2(3)");
        builder.Property(job => job.LastTriggeredAtUtc).HasColumnType("datetime2(3)");
        builder.Property(job => job.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(job => job.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.HasIndex(job => new { job.Enabled, job.NextRunAtUtc });
    }
}
