using AgentOS.Domain.Backlog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class BacklogItemConfiguration : IEntityTypeConfiguration<BacklogItem>
{
    public void Configure(EntityTypeBuilder<BacklogItem> builder)
    {
        builder.ToTable("BacklogItems");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Title).HasMaxLength(200).IsRequired();
        builder.Property(item => item.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(item => item.Priority).HasMaxLength(20).IsRequired();
        builder.Property(item => item.Status).HasMaxLength(20).IsRequired();
        builder.Property(item => item.EpicKey).HasMaxLength(100);
        builder.Property(item => item.Error).HasMaxLength(1000);
        builder.Property(item => item.RepairAttempt).HasDefaultValue(0);
        builder.Property(item => item.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(item => item.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.HasIndex(item => new { item.ProjectId, item.Status, item.CreatedAtUtc });
    }
}
