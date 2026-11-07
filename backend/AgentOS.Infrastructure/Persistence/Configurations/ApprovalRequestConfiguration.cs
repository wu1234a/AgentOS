using AgentOS.Domain.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("ApprovalRequests");
        builder.HasKey(request => request.Id);

        builder.Property(request => request.TargetType).HasConversion<int>().IsRequired();
        builder.Property(request => request.TargetId).HasMaxLength(128).IsRequired();
        builder.Property(request => request.Status).HasConversion<int>().IsRequired();
        builder.Property(request => request.RequestReason).HasMaxLength(1000);
        builder.Property(request => request.RequestedByUserId).HasMaxLength(128).IsRequired();
        builder.Property(request => request.DecidedByUserId).HasMaxLength(128);
        builder.Property(request => request.DecisionReason).HasMaxLength(2000);
        builder.Property(request => request.RequestedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(request => request.DecidedAtUtc).HasColumnType("datetime2(3)");

        builder.HasIndex(request => new { request.TargetType, request.TargetId })
            .IsUnique()
            .HasFilter("[Status] = 0")
            .HasDatabaseName("UX_ApprovalRequests_PendingTarget");

        builder.HasIndex(request => new { request.Status, request.RequestedAtUtc });
    }
}
