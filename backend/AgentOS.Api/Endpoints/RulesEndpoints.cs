using AgentOS.Api.Filters;
using AgentOS.Application.Rules;

namespace AgentOS.Api.Endpoints;

/// <summary>Rule management endpoints introduced by AOS-004.</summary>
public static class RulesEndpoints
{
    private const int MaxPage = 1_000_000;

    public static IEndpointRouteBuilder MapRuleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/rules")
            .AddEndpointFilter<ApiClientAuthenticationFilter>();

        group.MapGet("", ListAsync).WithName("ListRules");
        group.MapPost("/validate", ValidateAsync).WithName("ValidateRule");
        group.MapGet("/{ruleId:guid}", GetAsync).WithName("GetRule");
        group.MapPost("", CreateAsync).WithName("CreateRule");
        group.MapPut("/{ruleId:guid}", UpdateAsync).WithName("UpdateRule");
        group.MapPost("/{ruleId:guid}/enable", EnableAsync).WithName("EnableRule");
        group.MapPost("/{ruleId:guid}/disable", DisableAsync).WithName("DisableRule");

        return app;
    }

    private static async Task<IResult> ListAsync(
        RuleService service,
        int page = 1,
        int pageSize = 20,
        bool? enabled = null,
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (page is < 1 or > MaxPage)
        {
            errors["page"] = [$"page must be between 1 and {MaxPage}."];
        }

        if (pageSize is < 1 or > 100)
        {
            errors["pageSize"] = ["pageSize must be between 1 and 100."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await service.ListAsync(
            new RuleListQuery(page, pageSize, enabled, project),
            cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetAsync(
        Guid ruleId,
        RuleService service,
        CancellationToken cancellationToken)
    {
        var rule = await service.GetAsync(ruleId, cancellationToken);
        return rule is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Rule not found")
            : Results.Ok(rule);
    }

    private static async Task<IResult> CreateAsync(
        SaveRuleRequest request,
        RuleService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/v1/rules/{result.Rule!.Id}", result.Rule)
            : Results.ValidationProblem(result.Errors);
    }

    private static async Task<IResult> UpdateAsync(
        Guid ruleId,
        SaveRuleRequest request,
        RuleService service,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(ruleId, request, cancellationToken);

        return result switch
        {
            null => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Rule not found"),
            { IsSuccess: true } => Results.Ok(result.Rule),
            _ => Results.ValidationProblem(result.Errors)
        };
    }

    private static Task<IResult> EnableAsync(
        Guid ruleId,
        RuleService service,
        CancellationToken cancellationToken)
        => SetEnabledAsync(ruleId, enabled: true, service, cancellationToken);

    private static Task<IResult> DisableAsync(
        Guid ruleId,
        RuleService service,
        CancellationToken cancellationToken)
        => SetEnabledAsync(ruleId, enabled: false, service, cancellationToken);

    private static async Task<IResult> SetEnabledAsync(
        Guid ruleId,
        bool enabled,
        RuleService service,
        CancellationToken cancellationToken)
    {
        var result = await service.SetEnabledAsync(ruleId, enabled, cancellationToken);

        return result switch
        {
            null => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Rule not found"),
            { IsSuccess: true } => Results.Ok(result.Rule),
            _ => Results.ValidationProblem(result.Errors)
        };
    }

    private static async Task<IResult> ValidateAsync(
        SaveRuleRequest request,
        RuleService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ValidateAsync(request, cancellationToken);
        return Results.Ok(result);
    }
}
