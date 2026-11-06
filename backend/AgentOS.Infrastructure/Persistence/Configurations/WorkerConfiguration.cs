using AgentOS.Domain.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class WorkerConfiguration : IEntityTypeConfiguration<Worker>
{
    public void Configure(EntityTypeBuilder<Worker> builder)
    {
        builder.ToTable(
            "Workers",
            table => table.HasCheckConstraint(
                "CK_Workers_CapabilitiesJson_IsJson",
                "ISJSON([CapabilitiesJson]) = 1"));

        builder.HasKey(worker => worker.Id);

        builder.Property(worker => worker.WorkerCode).HasMaxLength(100).IsRequired();
        builder.Property(worker => worker.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(worker => worker.HostName).HasMaxLength(200).IsRequired();
        builder.Property(worker => worker.Platform).HasMaxLength(40).IsRequired();
        builder.Property(worker => worker.Architecture).HasMaxLength(40).IsRequired();
        builder.Property(worker => worker.Version).HasMaxLength(40).IsRequired();
        builder.Property(worker => worker.CapabilitiesJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(worker => worker.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(worker => worker.LastHeartbeatAtUtc).HasColumnType("datetime2(3)");
        builder.Property(worker => worker.RegisteredAtUtc).HasColumnType("datetime2(3)");
        builder.Property(worker => worker.UpdatedAtUtc).HasColumnType("datetime2(3)");

        builder.HasIndex(worker => worker.WorkerCode)
            .IsUnique()
            .HasDatabaseName("UX_Workers_WorkerCode");
    }
}
