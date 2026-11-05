using AgentOS.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");

        builder.HasKey(project => project.Id);

        builder.Property(project => project.Name)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(project => project.Slug)
            .HasMaxLength(80)
            .IsRequired();

        builder.HasIndex(project => project.Slug).IsUnique();

        builder.Property(project => project.IsEnabled).IsRequired();

        builder.Property(project => project.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(project => project.UpdatedAtUtc).HasColumnType("datetime2(3)");
    }
}
