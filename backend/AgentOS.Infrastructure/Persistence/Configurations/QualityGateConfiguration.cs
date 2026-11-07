using AgentOS.Domain.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class QualityGateRunConfiguration : IEntityTypeConfiguration<QualityGateRun>
{
    public void Configure(EntityTypeBuilder<QualityGateRun> builder)
    {
        builder.ToTable("QualityGateRuns");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Repository).HasMaxLength(200).IsRequired();
        builder.Property(run => run.HeadSha).HasMaxLength(64).IsRequired();
        builder.Property(run => run.WorkflowName).HasMaxLength(200).IsRequired();
        builder.Property(run => run.Status).HasMaxLength(30).IsRequired();
        builder.Property(run => run.Conclusion).HasMaxLength(30);
        builder.Property(run => run.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.HasIndex(run => new { run.Repository, run.HeadSha, run.WorkflowName }).IsUnique();
    }
}

public sealed class QualityGateEvaluationConfiguration : IEntityTypeConfiguration<QualityGateEvaluation>
{
    public void Configure(EntityTypeBuilder<QualityGateEvaluation> builder)
    {
        builder.ToTable("QualityGateEvaluations");
        builder.HasKey(evaluation => evaluation.Id);
        builder.Property(evaluation => evaluation.Repository).HasMaxLength(200).IsRequired();
        builder.Property(evaluation => evaluation.HeadSha).HasMaxLength(64).IsRequired();
        builder.Property(evaluation => evaluation.Status).HasMaxLength(20).IsRequired();
        builder.Property(evaluation => evaluation.ReasonCode).HasMaxLength(80).IsRequired();
        builder.Property(evaluation => evaluation.Reason).HasMaxLength(1000);
        builder.Property(evaluation => evaluation.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(evaluation => evaluation.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.HasIndex(evaluation => new { evaluation.Repository, evaluation.HeadSha }).IsUnique();
    }
}
