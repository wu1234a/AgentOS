using System.Data.Common;
using AgentOS.Application.ModelProfiles;
using AgentOS.Application.Tasks;
using AgentOS.Domain.Tasks;
using AgentOS.Domain.Workers;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOS.Infrastructure.Tasks;

/// <summary>
/// Atomic claim implementation.
/// </summary>
/// <remarks>
/// Selection takes no locks: each candidate is claimed by a conditional
/// <c>UPDATE ... OUTPUT</c> on its primary key, so a lost race affects zero rows and the caller
/// refills its batch from the next selection round.
/// </remarks>
public sealed class TaskClaimer(
    AgentOSDbContext dbContext,
    LeaseTokenService tokens,
    TimeProvider timeProvider,
    IOptions<TaskQueueOptions> options,
    ISkillContentReader skillReader,
    IModelProfileRepository modelProfiles,
    ILogger<TaskClaimer> logger) : ITaskClaimer
{
    private const int SelectionRounds = 4;

    private const string SelectCandidatesSql = """
        SELECT TOP (@capacity)
               t.[Id], p.[Slug], a.[Code] AS AgentCode, a.[Role] AS AgentRole,
               s.[Code] AS SkillCode, s.[RelativePath] AS SkillRelativePath
        FROM [Tasks] AS t
        INNER JOIN [Projects] AS p ON p.[Id] = t.[ProjectId] AND p.[IsEnabled] = 1
        INNER JOIN [Agents] AS a ON a.[Id] = t.[AgentId]
        LEFT JOIN [Skills] AS s ON s.[Id] = t.[SkillId]
        WHERE t.[Status] IN (N'Queued', N'RetryWait')
          AND t.[NextRunAtUtc] <= @now
        ORDER BY CASE t.[Priority]
                   WHEN N'urgent' THEN 0
                   WHEN N'high' THEN 1
                   WHEN N'normal' THEN 2
                   ELSE 3
                 END,
                 t.[CreatedAtUtc],
                 t.[Id];
        """;

    private const string ClaimOneSql = """
        UPDATE [Tasks]
        SET [Status] = N'Running',
            [LeaseOwnerWorkerId] = @workerId,
            [LeaseTokenHash] = @tokenHash,
            [LeaseExpiresAtUtc] = @leaseExpiresAtUtc,
            [LastHeartbeatAtUtc] = @now,
            [StartedAtUtc] = COALESCE([StartedAtUtc], @now),
            [AttemptCount] = [AttemptCount] + 1,
            [UpdatedAtUtc] = @now
        OUTPUT inserted.[Id], inserted.[AttemptCount], inserted.[MaxAttempts], inserted.[TimeoutSeconds],
               inserted.[TaskCode], inserted.[Title], inserted.[Priority], inserted.[InputJson],
               inserted.[RuleId], inserted.[RuleVersion], inserted.[AgentId], inserted.[SkillId],
               inserted.[ModelProfileId]
        WHERE [Id] = @taskId
          AND [Status] IN (N'Queued', N'RetryWait')
          AND [NextRunAtUtc] <= @now;
        """;

    public async Task<IReadOnlyList<ClaimedTask>> ClaimAsync(
        Guid workerId,
        int capacity,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(capacity, 1, Math.Max(1, options.Value.ClaimBatchLimit));
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var leaseExpiresAtUtc = now.Add(options.Value.LeaseDuration);

        // Defence in depth: the API filter already rejects disabled runners.
        var workerStatus = await dbContext.Workers
            .AsNoTracking()
            .Where(worker => worker.Id == workerId)
            .Select(worker => (WorkerStatus?)worker.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (workerStatus is null || workerStatus == WorkerStatus.Disabled)
        {
            return [];
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var dbTransaction = transaction.GetDbTransaction();
        var connection = dbContext.Database.GetDbConnection();

        var claimed = new List<ClaimedTask>(limit);

        for (var round = 0; round < SelectionRounds && claimed.Count < limit; round++)
        {
            var candidates = await SelectCandidatesAsync(
                connection,
                dbTransaction,
                limit - claimed.Count,
                now,
                cancellationToken);

            if (candidates.Count == 0)
            {
                break;
            }

            var progress = 0;

            foreach (var candidate in candidates)
            {
                if (claimed.Count == limit)
                {
                    break;
                }

                var (token, tokenHash) = tokens.Create();
                var claimedRow = await TryClaimAsync(
                    connection,
                    dbTransaction,
                    candidate.Id,
                    workerId,
                    tokenHash,
                    leaseExpiresAtUtc,
                    now,
                    cancellationToken);

                if (claimedRow is null)
                {
                    continue;
                }

                var attemptId = Guid.CreateVersion7();
                dbContext.TaskAttempts.Add(new TaskAttempt
                {
                    Id = attemptId,
                    TaskId = claimedRow.Id,
                    WorkerId = workerId,
                    AttemptNumber = claimedRow.AttemptCount,
                    Status = AttemptStatus.Running,
                    StartedAtUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                var skillContent = await skillReader.ReadAsync(candidate.SkillRelativePath, cancellationToken);
                var modelProfile = await modelProfiles.ResolveAsync(claimedRow.Id, null, null, cancellationToken);

                claimed.Add(new ClaimedTask(
                    claimedRow.Id,
                    candidate.ProjectSlug,
                    claimedRow.TaskCode,
                    claimedRow.Title,
                    claimedRow.Priority,
                    claimedRow.InputJson,
                    candidate.AgentCode,
                    candidate.AgentRole,
                    candidate.SkillCode,
                    skillContent,
                    claimedRow.RuleId,
                    claimedRow.RuleVersion,
                    claimedRow.AgentId,
                    claimedRow.SkillId,
                    claimedRow.ModelProfileId,
                    claimedRow.MaxAttempts,
                    claimedRow.TimeoutSeconds,
                    new TaskLease(attemptId, claimedRow.AttemptCount, token, leaseExpiresAtUtc),
                    modelProfile));

                progress++;
            }

            if (progress == 0)
            {
                break;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (claimed.Count > 0)
        {
            logger.LogInformation(
                "Worker {WorkerId} claimed {Count} task(s): {TaskIds}.",
                workerId,
                claimed.Count,
                string.Join(",", claimed.Select(task => task.TaskId)));
        }

        return claimed;
    }

    private static async Task<List<Candidate>> SelectCandidatesAsync(
        DbConnection connection,
        DbTransaction transaction,
        int capacity,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var candidates = new List<Candidate>(capacity);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SelectCandidatesSql;
        AddParameter(command, "@capacity", capacity);
        AddParameter(command, "@now", now);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new Candidate(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return candidates;
    }

    private static async Task<ClaimedRow?> TryClaimAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid taskId,
        Guid workerId,
        string tokenHash,
        DateTime leaseExpiresAtUtc,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ClaimOneSql;
        AddParameter(command, "@workerId", workerId);
        AddParameter(command, "@tokenHash", tokenHash);
        AddParameter(command, "@leaseExpiresAtUtc", leaseExpiresAtUtc);
        AddParameter(command, "@now", now);
        AddParameter(command, "@taskId", taskId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ClaimedRow(
            reader.GetGuid(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8),
            reader.IsDBNull(9) ? null : reader.GetInt32(9),
            reader.GetGuid(10),
            reader.IsDBNull(11) ? null : reader.GetGuid(11),
            reader.IsDBNull(12) ? null : reader.GetGuid(12));
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record Candidate(
        Guid Id,
        string ProjectSlug,
        string AgentCode,
        string AgentRole,
        string? SkillCode,
        string? SkillRelativePath);

    private sealed record ClaimedRow(
        Guid Id,
        int AttemptCount,
        int MaxAttempts,
        int TimeoutSeconds,
        string TaskCode,
        string Title,
        string Priority,
        string InputJson,
        Guid? RuleId,
        int? RuleVersion,
        Guid AgentId,
        Guid? SkillId,
        Guid? ModelProfileId);
}
