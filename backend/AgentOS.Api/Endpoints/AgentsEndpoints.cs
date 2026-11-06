using AgentOS.Api.Filters;
using AgentOS.Application.Agents;

namespace AgentOS.Api.Endpoints;

/// <summary>Agent registry endpoints (AOS-009).</summary>
public static class AgentsEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/agents").AddEndpointFilter<ApiClientAuthenticationFilter>();

        group.MapGet("", ListAsync).WithName("ListAgents");
        group.MapGet("/{agentId:guid}", GetAsync).WithName("GetAgent");
        group.MapPost("", CreateAsync).WithName("CreateAgent");
        group.MapPut("/{agentId:guid}", UpdateAsync).WithName("UpdateAgent");
        group.MapPost("/{agentId:guid}/enable", EnableAsync).WithName("EnableAgent");
        group.MapPost("/{agentId:guid}/disable", DisableAsync).WithName("DisableAgent");
        group.MapPost("/{agentId:guid}/archive", ArchiveAsync).WithName("ArchiveAgent");

        return app;
    }

    private static async Task<IResult> ListAsync(
        AgentService service,
        Guid? projectId,
        string? role,
        bool? includeArchived,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(limit ?? 50, 1, 200);
        var (items, total) = await service.ListAsync(
            projectId,
            role,
            includeArchived ?? false,
            pageSize,
            Math.Max(0, offset ?? 0),
            cancellationToken);

        return Results.Ok(new { items, total });
    }

    private static async Task<IResult> GetAsync(
        Guid agentId,
        AgentService service,
        CancellationToken cancellationToken)
    {
        var agent = await service.GetAsync(agentId, cancellationToken);
        return agent is null ? Results.NotFound() : Results.Ok(agent);
    }

    private static async Task<IResult> CreateAsync(
        CreateAgentRequest request,
        AgentService service,
        CancellationToken cancellationToken)
        => Map(await service.CreateAsync(request, cancellationToken));

    private static async Task<IResult> UpdateAsync(
        Guid agentId,
        UpdateAgentRequest request,
        AgentService service,
        CancellationToken cancellationToken)
        => Map(await service.UpdateAsync(agentId, request, cancellationToken));

    private static async Task<IResult> EnableAsync(
        Guid agentId,
        AgentService service,
        CancellationToken cancellationToken)
        => Map(await service.EnableAsync(agentId, cancellationToken));

    private static async Task<IResult> DisableAsync(
        Guid agentId,
        AgentService service,
        CancellationToken cancellationToken)
        => Map(await service.DisableAsync(agentId, cancellationToken));

    private static async Task<IResult> ArchiveAsync(
        Guid agentId,
        AgentService service,
        CancellationToken cancellationToken)
        => Map(await service.ArchiveAsync(agentId, cancellationToken));

    private static IResult Map(AgentResult result)
        => result.Outcome switch
        {
            AgentOutcome.Ok => Results.Ok(result.Agent),
            AgentOutcome.NotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Agent not found"),
            AgentOutcome.Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Agent lifecycle conflict",
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode }),
            _ => Results.ValidationProblem(result.Errors)
        };
}
