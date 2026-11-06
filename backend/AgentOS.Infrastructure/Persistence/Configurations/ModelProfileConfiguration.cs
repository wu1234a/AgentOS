using AgentOS.Domain.ModelProfiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class ModelProfileConfiguration : IEntityTypeConfiguration<ModelProfile>
{
    public void Configure(EntityTypeBuilder<ModelProfile> builder)
    {
        builder.ToTable(
            "ModelProfiles",
            table => table.HasCheckConstraint(
                "CK_ModelProfiles_ConfigurationJson_IsJson",
                "ISJSON([ConfigurationJson]) = 1"));

        builder.HasKey(profile => profile.Id);

        builder.Property(profile => profile.Code).HasMaxLength(80).IsRequired();
        builder.Property(profile => profile.Provider).HasMaxLength(60).IsRequired();
        builder.Property(profile => profile.Model).HasMaxLength(120).IsRequired();
        builder.Property(profile => profile.ExecutionMode).HasMaxLength(20).IsRequired();
        builder.Property(profile => profile.ConfigurationJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(profile => profile.SecretReference).HasMaxLength(200);
        builder.Property(profile => profile.MaxCostPerTask).HasPrecision(18, 6);
        builder.Property(profile => profile.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(profile => profile.UpdatedAtUtc).HasColumnType("datetime2(3)");

        builder.HasIndex(profile => profile.Code)
            .IsUnique()
            .HasDatabaseName("UX_ModelProfiles_Code");
    }
}
