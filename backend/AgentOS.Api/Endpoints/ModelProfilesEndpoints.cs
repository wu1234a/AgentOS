using AgentOS.Api.Filters;
using AgentOS.Application.ModelProfiles;

namespace AgentOS.Api.Endpoints;

/// <summary>Model/Provider profile registry and resolution endpoints (AOS-010).</summary>
public static class ModelProfilesEndpoints
{
    public static IEndpointRouteBuilder MapModelProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/model-profiles").AddEndpointFilter<ApiClientAuthenticationFilter>();

        group.MapGet("", ListAsync).WithName("ListModelProfiles");
        group.MapGet("/resolve", ResolveAsync).WithName("ResolveModelProfile");
        group.MapGet("/{profileId:guid}", GetAsync).WithName("GetModelProfile");
        group.MapPost("", CreateAsync).WithName("CreateModelProfile");
        group.MapPut("/{profileId:guid}", UpdateAsync).WithName("UpdateModelProfile");
        group.MapPost("/{profileId:guid}/enable", EnableAsync).WithName("EnableModelProfile");
        group.MapPost("/{profileId:guid}/disable", DisableAsync).WithName("DisableModelProfile");

        return app;
    }

    private static async Task<IResult> ListAsync(
        ModelProfileService service,
        bool? enabled,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var (items, total) = await service.ListAsync(
            enabled,
            Math.Clamp(limit ?? 50, 1, 200),
            Math.Max(0, offset ?? 0),
            cancellationToken);

        return Results.Ok(new { items, total });
    }

    private static async Task<IResult> ResolveAsync(
        ModelProfileService service,
        Guid? taskId,
        Guid? ruleId,
        Guid? agentId,
        CancellationToken cancellationToken)
    {
        if (taskId is null && ruleId is null && agentId is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Provide at least one of taskId, ruleId or agentId."]
            });
        }

        var resolved = await service.ResolveAsync(taskId, ruleId, agentId, cancellationToken);
        return resolved is null
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Model profile unresolved",
                extensions: new Dictionary<string, object?> { ["code"] = "model-profile-unresolved" })
            : Results.Ok(resolved);
    }

    private static async Task<IResult> GetAsync(
        Guid profileId,
        ModelProfileService service,
        CancellationToken cancellationToken)
    {
        var profile = await service.GetAsync(profileId, cancellationToken);
        return profile is null ? Results.NotFound() : Results.Ok(profile);
    }

    private static async Task<IResult> CreateAsync(
        CreateModelProfileRequest request,
        ModelProfileService service,
        CancellationToken cancellationToken)
        => Map(await service.CreateAsync(request, cancellationToken));

    private static async Task<IResult> UpdateAsync(
        Guid profileId,
        UpdateModelProfileRequest request,
        ModelProfileService service,
        CancellationToken cancellationToken)
        => Map(await service.UpdateAsync(profileId, request, cancellationToken));

    private static async Task<IResult> EnableAsync(
        Guid profileId,
        ModelProfileService service,
        CancellationToken cancellationToken)
        => Map(await service.EnableAsync(profileId, cancellationToken));

    private static async Task<IResult> DisableAsync(
        Guid profileId,
        ModelProfileService service,
        CancellationToken cancellationToken)
        => Map(await service.DisableAsync(profileId, cancellationToken));

    private static IResult Map(ModelProfileResult result)
        => result.Outcome switch
        {
            ModelProfileOutcome.Ok => Results.Ok(result.Profile),
            ModelProfileOutcome.NotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Model profile not found"),
            ModelProfileOutcome.Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Model profile conflict",
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode }),
            _ => Results.ValidationProblem(result.Errors)
        };
}
