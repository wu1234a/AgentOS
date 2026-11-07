using AgentOS.Domain.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class TelegramNotificationConfiguration : IEntityTypeConfiguration<TelegramNotification>
{
    public void Configure(EntityTypeBuilder<TelegramNotification> builder)
    {
        builder.ToTable("TelegramNotifications");
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Status).HasMaxLength(20).IsRequired();
        builder.Property(notification => notification.LastError).HasMaxLength(1000);
        builder.Property(notification => notification.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(notification => notification.SentAtUtc).HasColumnType("datetime2(3)");
        builder.Property(notification => notification.NextRetryAtUtc).HasColumnType("datetime2(3)");
        builder.HasIndex(notification => notification.ApprovalId).IsUnique();
        builder.HasIndex(notification => new { notification.Status, notification.NextRetryAtUtc });
    }
}

public sealed class TelegramPollerStateConfiguration : IEntityTypeConfiguration<TelegramPollerState>
{
    public void Configure(EntityTypeBuilder<TelegramPollerState> builder)
    {
        builder.ToTable("TelegramPollerState");
        builder.HasKey(state => state.Id);
    }
}

public sealed class TelegramProcessedUpdateConfiguration : IEntityTypeConfiguration<TelegramProcessedUpdate>
{
    public void Configure(EntityTypeBuilder<TelegramProcessedUpdate> builder)
    {
        builder.ToTable("TelegramProcessedUpdates");
        builder.HasKey(update => update.UpdateId);
        builder.Property(update => update.ProcessedAtUtc).HasColumnType("datetime2(3)");
    }
}
