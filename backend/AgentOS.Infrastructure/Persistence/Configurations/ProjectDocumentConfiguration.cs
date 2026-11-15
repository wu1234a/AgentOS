using AgentOS.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class ProjectDocumentConfiguration : IEntityTypeConfiguration<ProjectDocument>
{
    public void Configure(EntityTypeBuilder<ProjectDocument> builder)
    {
        builder.ToTable("ProjectDocuments");
        builder.HasKey(document => document.Id);
        builder.Property(document => document.DocumentType).HasMaxLength(40).IsRequired();
        builder.Property(document => document.Title).HasMaxLength(200).IsRequired();
        builder.Property(document => document.ContentMarkdown).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(document => document.Status).HasMaxLength(20).IsRequired();
        builder.Property(document => document.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.HasIndex(document => new { document.ProjectId, document.DocumentType, document.Version }).IsUnique();
        builder.HasIndex(document => new { document.ProjectId, document.Status, document.CreatedAtUtc });
    }
}
