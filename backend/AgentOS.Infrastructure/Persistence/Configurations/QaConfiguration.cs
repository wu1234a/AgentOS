using AgentOS.Domain.Qa;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class QaRunConfiguration : IEntityTypeConfiguration<QaRun>
{
    public void Configure(EntityTypeBuilder<QaRun> builder)
    {
        builder.ToTable("QaRuns");
        builder.HasKey(run => run.Id);

        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(run => run.Conclusion).HasConversion<string>().HasMaxLength(20);
        builder.Property(run => run.ErrorSummary).HasMaxLength(2000);
        builder.Property(run => run.CreatedAtUtc).HasColumnType("datetime2(3)");

        builder.HasOne<Domain.CodingRuns.CodingRun>()
            .WithMany()
            .HasForeignKey(run => run.CodingRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(run => new { run.CodingRunId, run.AttemptNo }).IsUnique();
    }
}

public sealed class QaCommandResultConfiguration : IEntityTypeConfiguration<QaCommandResult>
{
    public void Configure(EntityTypeBuilder<QaCommandResult> builder)
    {
        builder.ToTable("QaCommandResults");
        builder.HasKey(result => result.Id);

        builder.Property(result => result.CommandText).HasMaxLength(1000).IsRequired();
        builder.Property(result => result.Status).HasMaxLength(20).IsRequired();
        builder.Property(result => result.OutputExcerpt).HasMaxLength(4000);

        builder.HasOne<QaRun>()
            .WithMany()
            .HasForeignKey(result => result.QaRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(result => new { result.QaRunId, result.Ordinal }).IsUnique();
    }
}
