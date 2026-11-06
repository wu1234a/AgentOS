using AgentOS.Api.Filters;
using AgentOS.Application.Skills;

namespace AgentOS.Api.Endpoints;

/// <summary>Skill version registry endpoints (AOS-011).</summary>
public static class SkillsEndpoints
{
    public static IEndpointRouteBuilder MapSkillEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/skills").AddEndpointFilter<ApiClientAuthenticationFilter>();

        group.MapGet("", ListAsync).WithName("ListSkills");
        group.MapGet("/{skillId:guid}", GetAsync).WithName("GetSkill");
        group.MapGet("/{skillId:guid}/content", GetContentAsync).WithName("GetSkillContent");
        group.MapPost("", CreateAsync).WithName("CreateSkill");
        group.MapPut("/{skillId:guid}", UpdateAsync).WithName("UpdateSkill");
        group.MapPost("/{skillId:guid}/enable", EnableAsync).WithName("EnableSkill");
        group.MapPost("/{skillId:guid}/disable", DisableAsync).WithName("DisableSkill");

        return app;
    }

    private static async Task<IResult> ListAsync(
        SkillService service,
        Guid? projectId,
        string? code,
        bool? includeDisabled,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var (items, total) = await service.ListAsync(
            projectId,
            code,
            includeDisabled ?? false,
            Math.Clamp(limit ?? 50, 1, 200),
            Math.Max(0, offset ?? 0),
            cancellationToken);

        return Results.Ok(new { items, total });
    }

    private static async Task<IResult> GetAsync(
        Guid skillId,
        SkillService service,
        CancellationToken cancellationToken)
    {
        var skill = await service.GetAsync(skillId, cancellationToken);
        return skill is null ? Results.NotFound() : Results.Ok(skill);
    }

    private static async Task<IResult> GetContentAsync(
        Guid skillId,
        SkillService service,
        CancellationToken cancellationToken)
    {
        var content = await service.GetContentAsync(skillId, cancellationToken);
        return content is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Skill content not found")
            : Results.Text(content, "text/markdown");
    }

    private static async Task<IResult> CreateAsync(
        CreateSkillRequest request,
        SkillService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return result.Outcome == SkillOutcome.Ok
            ? Results.Created($"/api/v1/skills/{result.Skill!.Id}", result.Skill)
            : Map(result);
    }

    private static async Task<IResult> UpdateAsync(
        Guid skillId,
        UpdateSkillRequest request,
        SkillService service,
        CancellationToken cancellationToken)
        => Map(await service.UpdateAsync(skillId, request, cancellationToken));

    private static async Task<IResult> EnableAsync(
        Guid skillId,
        SkillService service,
        CancellationToken cancellationToken)
        => Map(await service.EnableAsync(skillId, cancellationToken));

    private static async Task<IResult> DisableAsync(
        Guid skillId,
        SkillService service,
        CancellationToken cancellationToken)
        => Map(await service.DisableAsync(skillId, cancellationToken));

    private static IResult Map(SkillResult result)
        => result.Outcome switch
        {
            SkillOutcome.Ok => Results.Ok(result.Skill),
            SkillOutcome.NotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Skill not found"),
            SkillOutcome.Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Skill version conflict",
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode }),
            SkillOutcome.StorageUnavailable => Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Skill storage is not configured",
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode }),
            _ => Results.ValidationProblem(result.Errors)
        };
}
