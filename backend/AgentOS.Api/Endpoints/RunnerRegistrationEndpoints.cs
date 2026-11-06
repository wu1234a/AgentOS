using AgentOS.Application.Runners;

namespace AgentOS.Api.Endpoints;

/// <summary>Runner registration endpoint (AOS-006).</summary>
public static class RunnerRegistrationEndpoints
{
    public static IEndpointRouteBuilder MapRunnerRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/runners/register", RegisterAsync).WithName("RegisterRunner");
        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RunnerRegistrationRequest request,
        HttpContext httpContext,
        RunnerRegistrationService service,
        CancellationToken cancellationToken)
    {
        var key = httpContext.Request.Headers["X-AgentOS-Registration-Key"].FirstOrDefault();
        var result = await service.RegisterAsync(key, request, cancellationToken);

        return result.Outcome switch
        {
            RunnerRegistrationOutcome.Registered => Results.Created(
                $"/api/v1/workers/{result.Registration!.WorkerId}",
                new
                {
                    workerId = result.Registration.WorkerId,
                    clientId = result.Registration.ClientCode,
                    clientSecret = result.Registration.ClientSecret,
                    scopes = result.Registration.Scopes,
                    createdAtUtc = result.Registration.CreatedAtUtc
                }),

            RunnerRegistrationOutcome.Disabled => Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Runner registration is disabled",
                detail: "Runner:RegistrationKey is not configured on this server."),

            RunnerRegistrationOutcome.Unauthorized => Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Registration rejected",
                detail: "A valid registration key is required."),

            _ => Results.ValidationProblem(result.Errors)
        };
    }
}
