using AgentOS.Domain.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.ToTable("KnowledgeDocuments");
        builder.HasKey(document => document.Id);
        builder.Property(document => document.Title).HasMaxLength(200).IsRequired();
        builder.Property(document => document.Content).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(document => document.TagsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(document => document.Source).HasMaxLength(100).IsRequired();
        builder.Property(document => document.EmbeddingJson).HasColumnType("nvarchar(max)");
        builder.Property(document => document.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(document => document.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.HasIndex(document => new { document.ProjectId, document.UpdatedAtUtc });
    }
}
