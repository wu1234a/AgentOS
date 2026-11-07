using System.Text.RegularExpressions;
using AgentOS.Api.Filters;
using AgentOS.Api.GitHub;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Endpoints;

public static class QualityGateEndpoints
{
    public static IEndpointRouteBuilder MapQualityGateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/quality-gates").AddEndpointFilter<ApiClientAuthenticationFilter>();
        group.MapPost("/reevaluate", ReevaluateAsync);
        group.MapGet("/branch-protection", BranchProtectionAsync);
        return app;
    }

    public sealed class ReevaluateRequest
    {
        public string? Repository { get; init; }
        public string? HeadSha { get; init; }
    }

    private static async Task<IResult> ReevaluateAsync(
        ReevaluateRequest request,
        QualityGateService service,
        IOptions<GitHubOptions> options,
        CancellationToken cancellationToken)
    {
        var repository = request.Repository?.Trim() ?? string.Empty;
        var sha = request.HeadSha?.Trim() ?? string.Empty;
        if (!options.Value.QualityGates.ContainsKey(repository))
        {
            return Results.BadRequest(new { error = "repository-not-configured" });
        }

        if (!Regex.IsMatch(sha, "^[0-9a-fA-F]{40}$"))
        {
            return Results.BadRequest(new { error = "invalid-head-sha" });
        }

        var (success, error) = await service.ReevaluateAsync(repository, sha, cancellationToken);
        return success
            ? Results.Ok(new { status = "reevaluated" })
            : Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Quality gate reevaluation failed",
                extensions: new Dictionary<string, object?> { ["code"] = error });
    }

    private static async Task<IResult> BranchProtectionAsync(
        string? repository,
        string? branch,
        QualityGateService service,
        IOptions<GitHubOptions> options,
        CancellationToken cancellationToken)
    {
        var repo = repository?.Trim() ?? string.Empty;
        var target = branch?.Trim() ?? string.Empty;
        if (!options.Value.QualityGates.ContainsKey(repo) || target.Length == 0)
        {
            return Results.BadRequest(new { error = "invalid-request" });
        }

        var (status, reason) = await service.CheckBranchProtectionAsync(repo, target, cancellationToken);
        return Results.Ok(new { repository = repo, branch = target, context = "agentos/quality-gate", status, reasonCode = reason });
    }
}
