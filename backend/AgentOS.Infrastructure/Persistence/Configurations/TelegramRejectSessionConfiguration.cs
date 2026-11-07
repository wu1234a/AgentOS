using AgentOS.Domain.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class TelegramRejectSessionConfiguration : IEntityTypeConfiguration<TelegramRejectSession>
{
    public void Configure(EntityTypeBuilder<TelegramRejectSession> builder)
    {
        builder.ToTable("TelegramRejectSessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Status).HasMaxLength(20).IsRequired();
        builder.Property(session => session.ExpiresAtUtc).HasColumnType("datetime2(3)");
        builder.Property(session => session.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.HasIndex(session => new { session.ChatId, session.UserId, session.Status });
        builder.HasIndex(session => session.ApprovalId).IsUnique();
    }
}
