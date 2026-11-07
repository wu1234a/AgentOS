using AgentOS.Api.Filters;
using AgentOS.Api.GitHub;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Endpoints;

public static class MergeReadinessEndpoints
{
    public static IEndpointRouteBuilder MapMergeReadinessEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/merge-readiness", EvaluateAsync)
            .AddEndpointFilter<ApiClientAuthenticationFilter>();
        return app;
    }

    private static async Task<IResult> EvaluateAsync(
        string? repository,
        int? number,
        MergeReadinessService service,
        IOptions<GitHubOptions> options,
        CancellationToken cancellationToken)
    {
        var repo = repository?.Trim() ?? string.Empty;
        if (number is null or <= 0 || !options.Value.MergePolicies.ContainsKey(repo))
        {
            return Results.BadRequest(new { error = "invalid-request" });
        }

        var result = await service.EvaluateAsync(repo, number.Value, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }
}
