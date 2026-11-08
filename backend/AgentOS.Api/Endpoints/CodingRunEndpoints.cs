using AgentOS.Api.Filters;
using AgentOS.Application.CodingRuns;
using AgentOS.Application.Qa;
using AgentOS.Application.Reviews;

namespace AgentOS.Api.Endpoints;

/// <summary>Coding Run control-plane endpoints (AOS-013).</summary>
public static class CodingRunEndpoints
{
    public static IEndpointRouteBuilder MapCodingRunEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/coding-runs").AddEndpointFilter<ApiClientAuthenticationFilter>();

        group.MapGet("", ListAsync).WithName("ListCodingRuns");
        group.MapGet("/{runId:guid}", GetAsync).WithName("GetCodingRun");
        group.MapPost("", CreateAsync).WithName("CreateCodingRun");
        group.MapPost("/{runId:guid}/cancel", CancelAsync).WithName("CancelCodingRun");
        group.MapPost("/{runId:guid}/retry", RetryAsync).WithName("RetryCodingRun");
        group.MapGet("/{runId:guid}/review", GetReviewAsync).WithName("GetCodingRunReview");
        group.MapPost("/{runId:guid}/review", CreateReviewAsync).WithName("CreateCodingRunReview");
        group.MapGet("/{runId:guid}/qa-runs/latest", GetLatestQaAsync).WithName("GetLatestQaRun");
        group.MapPost("/{runId:guid}/qa-runs", CreateQaAsync).WithName("CreateQaRun");

        return app;
    }

    private static async Task<IResult> ListAsync(
        CodingRunService service,
        string? status,
        Guid? projectId,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        var (items, total) = await service.ListAsync(
            status,
            projectId,
            Math.Clamp(limit ?? 50, 1, 200),
            Math.Max(0, offset ?? 0),
            cancellationToken);

        return Results.Ok(new { items, total });
    }

    private static async Task<IResult> GetAsync(
        Guid runId,
        CodingRunService service,
        CancellationToken cancellationToken)
    {
        var detail = await service.GetAsync(runId, cancellationToken);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> CreateAsync(
        CreateCodingRunRequest request,
        CodingRunService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return result.Outcome == CodingRunOutcome.Ok
            ? Results.Created($"/api/v1/coding-runs/{result.Run!.Id}", result.Run)
            : Map(result);
    }

    private static async Task<IResult> CancelAsync(
        Guid runId,
        CodingRunService service,
        CancellationToken cancellationToken)
        => Map(await service.CancelAsync(runId, cancellationToken));

    private static async Task<IResult> GetReviewAsync(
        Guid runId,
        ReviewService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(runId, cancellationToken);
        return result.Outcome == ReviewOutcome.NotFound
            ? Results.NotFound()
            : Results.Ok(result.Review);
    }

    private static async Task<IResult> CreateReviewAsync(
        Guid runId,
        ReviewService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateOrRetryAsync(runId, cancellationToken);
        return MapReview(result);
    }

    private static async Task<IResult> GetLatestQaAsync(
        Guid runId,
        QaService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetLatestAsync(runId, cancellationToken);
        return result.Outcome == QaOutcome.NotFound
            ? Results.NotFound()
            : Results.Ok(result.Qa);
    }

    private static async Task<IResult> CreateQaAsync(
        Guid runId,
        QaService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(runId, cancellationToken);
        return result.Outcome switch
        {
            QaOutcome.Ok => Results.Ok(result.Qa),
            QaOutcome.NotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Coding run not found"),
            _ => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "QA conflict",
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode })
        };
    }

    private static IResult MapReview(ReviewResult result)
        => result.Outcome switch
        {
            ReviewOutcome.Ok => Results.Ok(result.Review),
            ReviewOutcome.NotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Review not found"),
            _ => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Review conflict",
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode })
        };

    private static async Task<IResult> RetryAsync(
        Guid runId,
        CodingRunService service,
        CancellationToken cancellationToken)
        => Map(await service.RetryAsync(runId, cancellationToken));

    private static IResult Map(CodingRunResult result)
        => result.Outcome switch
        {
            CodingRunOutcome.Ok => Results.Ok(result.Run),
            CodingRunOutcome.NotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Coding run not found"),
            CodingRunOutcome.Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Coding run state conflict",
                extensions: new Dictionary<string, object?> { ["code"] = result.ErrorCode }),
            _ => Results.ValidationProblem(result.Errors)
        };
}
