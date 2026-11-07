using AgentOS.Api.Filters;
using AgentOS.Application.Approvals;
using AgentOS.Application.Decisions;

namespace AgentOS.Api.Endpoints;

public static class DecisionMemoryEndpoints
{
    public static IEndpointRouteBuilder MapDecisionMemoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/decision-memories").AddEndpointFilter<ApiClientAuthenticationFilter>();
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/{id:guid}/invalidate", InvalidateAsync);

        app.MapGet(
            "/api/v1/approval-requests/{approvalId:guid}/decision-memory-references",
            ReferencesAsync).AddEndpointFilter<ApiClientAuthenticationFilter>();
        return app;
    }

    private static async Task<IResult> ListAsync(
        DecisionMemoryService service,
        string? decision,
        string? lifecycle,
        string? scopeType,
        string? scopeKey,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit ?? 50, 1, 100);
        var skip = Math.Max(0, offset ?? 0);
        var items = await service.ListAsync(decision, lifecycle, scopeType, scopeKey, take, skip, cancellationToken);
        var total = await service.CountAsync(decision, lifecycle, scopeType, scopeKey, cancellationToken);
        return Results.Ok(new { items, total });
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        DecisionMemoryService service,
        CancellationToken cancellationToken)
    {
        var memory = await service.GetAsync(id, cancellationToken);
        return memory is null ? Results.NotFound() : Results.Ok(memory);
    }

    private static async Task<IResult> InvalidateAsync(
        Guid id,
        InvalidateDecisionRequest request,
        DecisionMemoryService service,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["reason"] = ["Invalidation reason is required."]
            });
        }

        return await service.InvalidateAsync(id, reason, cancellationToken)
            ? Results.Ok()
            : Results.NotFound();
    }

    private static async Task<IResult> ReferencesAsync(
        Guid approvalId,
        ApprovalService approvals,
        DecisionMemoryService service,
        CancellationToken cancellationToken)
    {
        var approval = await approvals.GetAsync(approvalId, cancellationToken);
        if (approval is null)
        {
            return Results.NotFound();
        }

        var references = await service.ReferencesAsync(approval.TargetType, approval.TargetId, cancellationToken);
        return Results.Ok(new { items = references, disclaimer = "历史决策仅供参考，不代表系统自动批准。" });
    }

    public sealed class InvalidateDecisionRequest
    {
        public string? Reason { get; init; }
    }
}
