using AgentOS.Domain.Approvals;
using Microsoft.Extensions.Logging;

namespace AgentOS.Application.Approvals;

public sealed class ApprovalService(
    IApprovalRepository repository,
    TimeProvider timeProvider,
    ILogger<ApprovalService> logger)
{
    public async Task<(IReadOnlyList<ApprovalView> Items, int Total)> ListAsync(
        string? status,
        string? targetType,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var rows = await repository.ListAsync(status, targetType, limit, offset, cancellationToken);
        var total = await repository.CountAsync(status, targetType, cancellationToken);
        return (rows.Select(ToView).ToList(), total);
    }

    public async Task<ApprovalView?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var request = await repository.GetByIdAsync(id, cancellationToken);
        return request is null ? null : ToView(request);
    }

    public async Task<ApprovalResult> CreateAsync(
        CreateApprovalRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ApprovalTargetType>(request.TargetType, ignoreCase: true, out var type))
        {
            return ApprovalResult.Invalid("target-type-invalid", "targetType", "Use CodingRun or PlannerDraft.");
        }

        var targetId = request.TargetId?.Trim() ?? string.Empty;
        if (targetId.Length is 0 or > 128)
        {
            return ApprovalResult.Invalid("target-id-invalid", "targetId", "Target id is required.");
        }

        if (!await repository.TargetExistsAsync(type, targetId, cancellationToken))
        {
            return ApprovalResult.NotFound();
        }

        if (await repository.GetPendingAsync(type, targetId, cancellationToken) is not null)
        {
            return ApprovalResult.Conflict("approval-already-pending");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var entity = new ApprovalRequest
        {
            Id = Guid.CreateVersion7(),
            TargetType = type,
            TargetId = targetId,
            Status = ApprovalStatus.Pending,
            RequestReason = Truncate(request.RequestReason, 1000),
            RequestedByUserId = "dev-user",
            RequestedAtUtc = now
        };

        await repository.AddAsync(entity, cancellationToken);
        await repository.SaveAsync(cancellationToken);
        logger.LogInformation("Approval {ApprovalId} created for {Type}/{Target}.", entity.Id, type, targetId);
        return ApprovalResult.Ok(ToView(entity));
    }

    public async Task<ApprovalResult> DecideAsync(
        Guid id,
        ApprovalDecisionRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ApprovalStatus>(request.Decision, ignoreCase: true, out var decision)
            || decision is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
        {
            return ApprovalResult.Invalid("decision-invalid", "decision", "Use Approved or Rejected.");
        }

        var entity = await repository.GetTrackedAsync(id, cancellationToken);
        if (entity is null)
        {
            return ApprovalResult.NotFound();
        }

        if (entity.Status != ApprovalStatus.Pending)
        {
            return ApprovalResult.Conflict("approval-already-decided");
        }

        var reason = Truncate(request.Reason, 2000);
        if (decision == ApprovalStatus.Rejected && string.IsNullOrWhiteSpace(reason))
        {
            return ApprovalResult.Invalid("reason-required", "reason", "Rejection reason is required.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        entity.Status = decision;
        entity.DecidedByUserId = "dev-user";
        entity.DecidedAtUtc = now;
        entity.DecisionReason = reason;
        await repository.SaveAsync(cancellationToken);

        logger.LogInformation("Approval {ApprovalId} decided {Decision}.", id, decision);
        return ApprovalResult.Ok(ToView(entity));
    }

    private static ApprovalView ToView(ApprovalRequest request)
        => new(
            request.Id,
            request.TargetType.ToString(),
            request.TargetId,
            request.Status.ToString(),
            request.RequestReason,
            request.RequestedByUserId,
            request.RequestedAtUtc,
            request.DecidedByUserId,
            request.DecidedAtUtc,
            request.DecisionReason);

    private static string? Truncate(string? value, int max)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length <= max ? text : text[..max];
    }
}
