using AgentOS.Domain.Projects;
using AgentOS.Domain.Security;
using AgentOS.Domain.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class ApiClientConfiguration : IEntityTypeConfiguration<ApiClient>
{
    public void Configure(EntityTypeBuilder<ApiClient> builder)
    {
        builder.ToTable(
            "ApiClients",
            table => table.HasCheckConstraint(
                "CK_ApiClients_ScopesJson_IsJson",
                "ISJSON([ScopesJson]) = 1"));

        builder.HasKey(client => client.Id);

        builder.Property(client => client.ClientCode).HasMaxLength(100).IsRequired();
        builder.Property(client => client.ClientType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(client => client.SecretHash).HasMaxLength(64).IsRequired();
        builder.Property(client => client.ScopesJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(client => client.ExpiresAtUtc).HasColumnType("datetime2(3)");
        builder.Property(client => client.LastUsedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(client => client.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(client => client.UpdatedAtUtc).HasColumnType("datetime2(3)");

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(client => client.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Worker>()
            .WithMany()
            .HasForeignKey(client => client.WorkerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(client => client.ClientCode)
            .IsUnique()
            .HasDatabaseName("UX_ApiClients_ClientCode");
    }
}
