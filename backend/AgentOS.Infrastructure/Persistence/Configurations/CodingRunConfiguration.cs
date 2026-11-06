using AgentOS.Domain.CodingRuns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgentOS.Infrastructure.Persistence.Configurations;

public sealed class CodingRunConfiguration : IEntityTypeConfiguration<CodingRun>
{
    public void Configure(EntityTypeBuilder<CodingRun> builder)
    {
        builder.ToTable("CodingRuns");
        builder.HasKey(run => run.Id);

        builder.Property(run => run.DraftContentHash).HasMaxLength(64).IsRequired();
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(run => run.SnapshotJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(run => run.ErrorCode).HasMaxLength(100);
        builder.Property(run => run.ErrorMessage).HasMaxLength(2000);
        builder.Property(run => run.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(run => run.UpdatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(run => run.StartedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(run => run.CompletedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(run => run.CancelRequestedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(run => run.RowVersion).IsRowVersion();

        builder.HasOne<Domain.Planning.PlanningDraft>()
            .WithMany()
            .HasForeignKey(run => run.DraftId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Projects.Project>()
            .WithMany()
            .HasForeignKey(run => run.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Agents.Agent>()
            .WithMany()
            .HasForeignKey(run => run.AgentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Tasks.AutomationTask>()
            .WithMany()
            .HasForeignKey(run => run.TaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(run => run.DraftId).IsUnique();
        builder.HasIndex(run => new { run.Status, run.CreatedAtUtc });
    }
}

public sealed class CodingRunEventConfiguration : IEntityTypeConfiguration<CodingRunEvent>
{
    public void Configure(EntityTypeBuilder<CodingRunEvent> builder)
    {
        builder.ToTable("CodingRunEvents");
        builder.HasKey(evt => evt.Id);

        builder.Property(evt => evt.EventType).HasMaxLength(40).IsRequired();
        builder.Property(evt => evt.FromStatus).HasMaxLength(20);
        builder.Property(evt => evt.ToStatus).HasMaxLength(20).IsRequired();
        builder.Property(evt => evt.DetailJson).HasMaxLength(4000);
        builder.Property(evt => evt.CreatedAtUtc).HasColumnType("datetime2(3)");

        builder.HasOne<CodingRun>()
            .WithMany()
            .HasForeignKey(evt => evt.CodingRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(evt => new { evt.CodingRunId, evt.Sequence }).IsUnique();
    }
}
