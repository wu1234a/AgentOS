using AgentOS.Application.Tasks;
using AgentOS.Domain.Tasks;
using AgentOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentOS.Infrastructure.Tasks;

/// <summary>
/// Persists runner log lines one by one so a retried batch containing already-stored sequences
/// keeps the new lines and silently skips the duplicates.
/// </summary>
public sealed class TaskLogStore(AgentOSDbContext dbContext) : ITaskLogStore
{
    private const int MaxMessageLength = 4000;

    public async Task AddAsync(
        Guid taskId,
        Guid attemptId,
        Guid workerId,
        IReadOnlyList<TaskLogEntry> entries,
        CancellationToken cancellationToken)
    {
        foreach (var entry in entries.OrderBy(candidate => candidate.Sequence))
        {
            var message = entry.Message ?? string.Empty;
            if (entry.Sequence <= 0 || message.Length == 0)
            {
                continue;
            }

            var entity = new TaskLog
            {
                Id = Guid.CreateVersion7(),
                TaskId = taskId,
                AttemptId = attemptId,
                WorkerId = workerId,
                Sequence = entry.Sequence,
                Level = NormalizeLevel(entry.Level),
                Message = message.Length <= MaxMessageLength ? message : message[..MaxMessageLength],
                CreatedAtUtc = entry.TimestampUtc?.ToUniversalTime() ?? DateTime.UtcNow
            };

            dbContext.TaskLogs.Add(entity);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (SqlErrorHelper.IsUniqueKeyViolation(exception))
            {
                // The same sequence was already stored by an earlier attempt of this batch.
                dbContext.Entry(entity).State = EntityState.Detached;
            }
        }
    }

    private static string NormalizeLevel(string? level)
        => level?.Trim().ToLowerInvariant() switch
        {
            "debug" => "debug",
            "warn" or "warning" => "warn",
            "error" => "error",
            _ => "info"
        };
}
