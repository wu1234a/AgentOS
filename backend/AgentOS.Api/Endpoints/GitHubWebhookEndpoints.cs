using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentOS.Api.GitHub;
using AgentOS.Application.Events;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Endpoints;

public static class GitHubWebhookEndpoints
{
    private const int MaxBodyBytes = 1_048_576;

    public static IEndpointRouteBuilder MapGitHubWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/webhooks/github", HandleAsync).WithName("GitHubWebhook");
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        EventService events,
        IOptions<GitHubOptions> options,
        IGitHubReviewQueue reviewQueue,
        QualityGateService qualityGates,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("GitHubWebhook");
        var configuration = options.Value;
        if (!configuration.Enabled)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(configuration.WebhookSecret)
            || configuration.AllowedRepositories.Length == 0)
        {
            logger.LogError("GitHub webhook is enabled but secret or repository allowlist is missing.");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (request.ContentType is null
            || !request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var delivery = request.Headers["X-GitHub-Delivery"].ToString();
        var eventName = request.Headers["X-GitHub-Event"].ToString();
        var signature = request.Headers["X-Hub-Signature-256"].ToString();
        if (string.IsNullOrWhiteSpace(delivery) || string.IsNullOrWhiteSpace(eventName) || string.IsNullOrWhiteSpace(signature))
        {
            return Results.BadRequest(new { error = "missing-required-header" });
        }

        var sizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = MaxBodyBytes;
        }

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var body = buffer.ToArray();
        if (!VerifySignature(body, configuration.WebhookSecret, signature))
        {
            logger.LogWarning("GitHub webhook signature rejected for delivery {Delivery}.", delivery);
            return Results.Unauthorized();
        }

        if (!configuration.AllowedEvents.Contains(eventName, StringComparer.OrdinalIgnoreCase))
        {
            return Results.Ok(new { deliveryId = delivery, status = "ignored" });
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "invalid-json" });
        }

        using (document)
        {
            var root = document.RootElement;
            var repository = root.TryGetProperty("repository", out var repositoryElement)
                && repositoryElement.TryGetProperty("full_name", out var fullName)
                ? fullName.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(repository)
                || !configuration.AllowedRepositories.Contains(repository, StringComparer.OrdinalIgnoreCase))
            {
                return Results.Ok(new { deliveryId = delivery, status = "ignored" });
            }

            if (!configuration.RepositoryProjects.TryGetValue(repository, out var projectSlug)
                || string.IsNullOrWhiteSpace(projectSlug))
            {
                logger.LogWarning("GitHub repository {Repository} has no project mapping.", repository);
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            (string SubjectId, string Summary, string? ResourceUrl, JsonElement Payload) normalized;
            try
            {
                normalized = Normalize(root, eventName, repository);
            }
            catch (Exception exception) when (exception is KeyNotFoundException
                or InvalidOperationException
                or FormatException
                or ArgumentException)
            {
                return Results.BadRequest(new { error = "invalid-payload" });
            }

            var eventRequest = new CreateEventRequest
            {
                SchemaVersion = "1.0",
                Source = "github",
                Project = projectSlug,
                EventType = $"github.{eventName}",
                ExternalEventId = delivery,
                DedupeKey = $"github:{delivery}",
                Subject = new EventSubjectRequest
                {
                    Type = eventName,
                    Id = normalized.SubjectId
                },
                Summary = normalized.Summary,
                ResourceUrl = normalized.ResourceUrl,
                OccurredAtUtc = DateTime.UtcNow.ToString("O"),
                Priority = "normal",
                Payload = normalized.Payload
            };

            var result = await events.CreateAsync(eventRequest, cancellationToken);

            if (configuration.ReviewEnabled
                && eventName == "pull_request"
                && root.TryGetProperty("action", out var prAction)
                && prAction.GetString() is "opened" or "synchronize"
                && root.TryGetProperty("pull_request", out var prElement)
                && prElement.TryGetProperty("head", out var head)
                && head.TryGetProperty("sha", out var headSha)
                && headSha.GetString() is { } sha)
            {
                await reviewQueue.EnqueueAsync(
                    repository,
                    prElement.GetProperty("number").GetInt32(),
                    sha,
                    prAction.GetString()!,
                    delivery,
                    cancellationToken);
            }

            if (eventName == "workflow_run"
                && root.TryGetProperty("workflow_run", out var workflowRun)
                && workflowRun.TryGetProperty("head_sha", out var workflowSha)
                && workflowSha.GetString() is { } workflowShaValue)
            {
                try
                {
                    await qualityGates.EvaluateAsync(
                        repository,
                        workflowShaValue,
                        workflowRun.TryGetProperty("name", out var workflowName) ? workflowName.GetString() ?? string.Empty : string.Empty,
                        workflowRun.TryGetProperty("id", out var runId) ? runId.GetInt64() : 0,
                        workflowRun.TryGetProperty("status", out var runStatus) ? runStatus.GetString() ?? string.Empty : string.Empty,
                        workflowRun.TryGetProperty("conclusion", out var conclusion) && conclusion.ValueKind == JsonValueKind.String
                            ? conclusion.GetString()
                            : null,
                        workflowRun.TryGetProperty("run_attempt", out var attempt) ? attempt.GetInt32() : 1,
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Quality gate evaluation failed for {Delivery}.", delivery);
                }
            }

            logger.LogInformation(
                "GitHub webhook {Delivery} {Event} {Outcome}.",
                delivery,
                eventName,
                result.Outcome);

            return result.Outcome switch
            {
                CreateEventOutcome.Created => Results.Accepted(value: new { deliveryId = delivery, status = "accepted" }),
                CreateEventOutcome.Duplicate or CreateEventOutcome.Conflict => Results.Ok(new { deliveryId = delivery, status = "duplicate" }),
                _ => Results.BadRequest(new { error = "invalid-event" })
            };
        }
    }

    internal static bool VerifySignature(byte[] body, string secret, string signature)
    {
        if (!signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var provided = signature[7..];
        try
        {
            var expected = Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(provided.ToLowerInvariant()));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static (string SubjectId, string Summary, string? ResourceUrl, JsonElement Payload) Normalize(
        JsonElement root,
        string eventName,
        string repository)
    {
        JsonElement payload;
        string subjectId;
        string summary;
        string? resourceUrl = null;

        if (eventName == "pull_request")
        {
            var pr = root.GetProperty("pull_request");
            subjectId = pr.GetProperty("number").GetInt64().ToString();
            summary = $"PR #{subjectId} {pr.GetProperty("title").GetString()} ({repository})";
            resourceUrl = pr.TryGetProperty("html_url", out var url) ? url.GetString() : null;
            payload = JsonSerializer.SerializeToElement(new
            {
                action = root.TryGetProperty("action", out var action) ? action.GetString() : null,
                number = pr.GetProperty("number").GetInt64(),
                state = pr.TryGetProperty("state", out var state) ? state.GetString() : null,
                title = pr.GetProperty("title").GetString(),
                htmlUrl = resourceUrl,
                draft = pr.TryGetProperty("draft", out var draft) && draft.GetBoolean(),
                merged = pr.TryGetProperty("merged", out var merged) && merged.GetBoolean(),
                user = pr.TryGetProperty("user", out var user) && user.TryGetProperty("login", out var login) ? login.GetString() : null,
                baseRef = pr.GetProperty("base").GetProperty("ref").GetString(),
                baseSha = pr.GetProperty("base").GetProperty("sha").GetString(),
                headRef = pr.GetProperty("head").GetProperty("ref").GetString(),
                headSha = pr.GetProperty("head").GetProperty("sha").GetString(),
                repository
            });
        }
        else if (eventName == "push")
        {
            var compare = root.TryGetProperty("compare", out var compareValue) ? compareValue.GetString() : null;
            var reference = root.GetProperty("ref").GetString() ?? "refs/heads/unknown";
            subjectId = reference;
            summary = $"Push {reference} ({repository})";
            resourceUrl = compare;
            payload = JsonSerializer.SerializeToElement(new
            {
                @ref = reference,
                before = root.TryGetProperty("before", out var before) ? before.GetString() : null,
                after = root.TryGetProperty("after", out var after) ? after.GetString() : null,
                created = root.TryGetProperty("created", out var created) && created.GetBoolean(),
                deleted = root.TryGetProperty("deleted", out var deleted) && deleted.GetBoolean(),
                forced = root.TryGetProperty("forced", out var forced) && forced.GetBoolean(),
                compare,
                headCommit = root.TryGetProperty("head_commit", out var head) && head.ValueKind == JsonValueKind.Object
                    && head.TryGetProperty("id", out var id) ? id.GetString() : null,
                repository
            });
        }
        else
        {
            var run = root.GetProperty("workflow_run");
            subjectId = run.GetProperty("id").GetInt64().ToString();
            summary = $"Workflow {run.GetProperty("name").GetString()} #{subjectId} ({repository})";
            resourceUrl = run.TryGetProperty("html_url", out var url) ? url.GetString() : null;
            payload = JsonSerializer.SerializeToElement(new
            {
                action = root.TryGetProperty("action", out var action) ? action.GetString() : null,
                id = run.GetProperty("id").GetInt64(),
                name = run.GetProperty("name").GetString(),
                runNumber = run.TryGetProperty("run_number", out var number) ? number.GetInt64() : 0,
                @event = run.TryGetProperty("event", out var runEvent) ? runEvent.GetString() : null,
                status = run.TryGetProperty("status", out var status) ? status.GetString() : null,
                conclusion = run.TryGetProperty("conclusion", out var conclusion) ? conclusion.GetString() : null,
                headBranch = run.TryGetProperty("head_branch", out var branch) ? branch.GetString() : null,
                headSha = run.TryGetProperty("head_sha", out var sha) ? sha.GetString() : null,
                htmlUrl = resourceUrl,
                repository
            });
        }

        return (subjectId, summary, resourceUrl, payload);
    }
}
