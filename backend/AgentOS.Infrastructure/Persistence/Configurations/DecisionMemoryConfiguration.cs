using AgentOS.Domain.Decisions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class DecisionMemoryConfiguration : IEntityTypeConfiguration<DecisionMemory>
{
    public void Configure(EntityTypeBuilder<DecisionMemory> builder)
    {
        builder.ToTable("DecisionMemories");
        builder.HasKey(memory => memory.Id);
        builder.Property(memory => memory.ScopeType).HasMaxLength(40).IsRequired();
        builder.Property(memory => memory.ScopeKey).HasMaxLength(160).IsRequired();
        builder.Property(memory => memory.Decision).HasMaxLength(20).IsRequired();
        builder.Property(memory => memory.Reason).HasMaxLength(2000);
        builder.Property(memory => memory.DecidedBy).HasMaxLength(128).IsRequired();
        builder.Property(memory => memory.InvalidatedBy).HasMaxLength(128);
        builder.Property(memory => memory.InvalidationReason).HasMaxLength(1000);
        builder.Property(memory => memory.DecidedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(memory => memory.ValidFromUtc).HasColumnType("datetime2(3)");
        builder.Property(memory => memory.ExpiresAtUtc).HasColumnType("datetime2(3)");
        builder.Property(memory => memory.InvalidatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(memory => memory.LastHitAtUtc).HasColumnType("datetime2(3)");
        builder.Property(memory => memory.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(memory => memory.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.HasIndex(memory => memory.SourceApprovalRequestId).IsUnique();
        builder.HasIndex(memory => new { memory.ScopeType, memory.ScopeKey, memory.DecidedAtUtc });
        builder.HasIndex(memory => new { memory.Decision, memory.ExpiresAtUtc });
    }
}
