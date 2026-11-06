using AgentOS.Domain.Projects;
using AgentOS.Domain.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> builder)
    {
        builder.ToTable("Skills");

        builder.HasKey(skill => skill.Id);

        builder.Property(skill => skill.Code).HasMaxLength(120).IsRequired();
        builder.Property(skill => skill.Version).HasMaxLength(40).IsRequired();
        builder.Property(skill => skill.RelativePath).HasMaxLength(500).IsRequired();
        builder.Property(skill => skill.ContentHash).HasMaxLength(64);
        builder.Property(skill => skill.Description).HasMaxLength(1000);
        builder.Property(skill => skill.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(skill => skill.UpdatedAtUtc).HasColumnType("datetime2(3)");

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(skill => skill.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        // A skill version is unique within its project; global skills (ProjectId IS NULL) are
        // matched by the filtered index on the project scope only.
        builder.HasIndex(skill => new { skill.ProjectId, skill.Code, skill.Version })
            .IsUnique()
            .HasFilter("[ProjectId] IS NOT NULL")
            .HasDatabaseName("UX_Skills_Project_Code_Version");

        // SQL Server treats NULLs as distinct, so global versions need their own filtered index.
        builder.HasIndex(skill => new { skill.Code, skill.Version })
            .IsUnique()
            .HasFilter("[ProjectId] IS NULL")
            .HasDatabaseName("UX_Skills_Global_Code_Version");
    }
}
