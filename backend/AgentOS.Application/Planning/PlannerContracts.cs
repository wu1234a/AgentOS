using AgentOS.Domain.Planning;

namespace AgentOS.Application.Planning;

public sealed record PlanningDraftView(
    Guid Id,
    Guid? ProjectId,
    string Title,
    string Goal,
    string? Context,
    Guid? ModelProfileId,
    string? ModelProfileCode,
    string Status,
    string ContentMarkdown,
    string? ErrorMessage,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? DecidedAtUtc);

public sealed class CreatePlanningDraftRequest
{
    public Guid? ProjectId { get; init; }

    public string? Title { get; init; }

    public string? Goal { get; init; }

    public string? Context { get; init; }

    public Guid? ModelProfileId { get; init; }
}

public enum PlannerOutcome
{
    Ok = 0,
    NotFound = 1,
    Conflict = 2,
    ValidationFailed = 3,
    ProviderError = 4,
    NotConfigured = 5
}

public sealed record PlannerResult(
    PlannerOutcome Outcome,
    PlanningDraftView? Draft,
    string? ErrorCode,
    IReadOnlyDictionary<string, string[]> Errors,
    string? ProviderDetail = null)
{
    public static PlannerResult Ok(PlanningDraftView draft) => new(PlannerOutcome.Ok, draft, null, Empty);

    public static PlannerResult NotFound() => new(PlannerOutcome.NotFound, null, "draft-not-found", Empty);

    public static PlannerResult Conflict(string code) => new(PlannerOutcome.Conflict, null, code, Empty);

    public static PlannerResult Invalid(IReadOnlyDictionary<string, string[]> errors)
        => new(PlannerOutcome.ValidationFailed, null, "draft-invalid", errors);

    public static PlannerResult ProviderError(string detail)
        => new(PlannerOutcome.ProviderError, null, "planner-provider-error", Empty, detail);

    public static PlannerResult NotConfigured(string code)
        => new(PlannerOutcome.NotConfigured, null, code, Empty);

    private static readonly Dictionary<string, string[]> Empty = [];
}

public sealed class PlannerOptions
{
    public string? DefaultModelProfileCode { get; set; } = "kimi-planner";

    public string[] AllowedProviderHosts { get; set; } = ["api.moonshot.cn", "api.openai.com"];

    public int MaxGoalCharacters { get; set; } = 4000;

    public int MaxContextCharacters { get; set; } = 8000;

    public int MaxDraftCharacters { get; set; } = 100_000;

    /// <summary>Preferred profile for the multi-agent project planning board.</summary>
    public string? ProjectPlanModelProfileCode { get; set; } = "deepseek-reviewer";

    /// <summary>Rough planning price estimate, USD per 1M prompt tokens.</summary>
    public decimal ProjectPlanInputPricePerMillion { get; set; } = 0.5m;

    /// <summary>Rough planning price estimate, USD per 1M completion tokens.</summary>
    public decimal ProjectPlanOutputPricePerMillion { get; set; } = 2m;

    /// <summary>Additional model profiles tried after the default Planner profile fails.</summary>
    public string[] BacklogFallbackProfileCodes { get; set; } = [];

    public PlannerAutoDispatchOptions AutoDispatch { get; set; } = new();
}

public sealed class PlannerAutoDispatchOptions
{
    public bool Enabled { get; set; }

    /// <summary>Only drafts belonging to this project slug are dispatched automatically.</summary>
    public string ProjectSlug { get; set; } = string.Empty;

    public string Source { get; set; } = "planner";

    public string EventType { get; set; } = "draft.approved";

    public string[] ValidationCommands { get; set; } = [];

    public PlannerAutoDeliveryOptions Delivery { get; set; } = new();
}

public sealed class PlannerAutoDeliveryOptions
{
    public string? Repository { get; set; }

    public string BaseBranch { get; set; } = "main";

    public string Remote { get; set; } = "origin";

    public string TokenEnvironmentVariable { get; set; } = "AGENTOS_GITHUB_TOKEN";

    public bool RequirePullRequest { get; set; } = true;
}

public sealed record ApiCompletionRequest(
    string BaseUrl,
    string ApiKey,
    string Model,
    string SystemPrompt,
    string UserPrompt,
    double? Temperature,
    int? MaxTokens,
    int TimeoutSeconds,
    string Provider);

public sealed record ApiCompletionResult(
    bool Success,
    string? Content,
    string? ErrorCode,
    string? ErrorMessage,
    int? StatusCode,
    int? PromptTokens = null,
    int? CompletionTokens = null);

/// <summary>OpenAI-compatible chat completion client used by the Planner (Kimi/OpenAI).</summary>
public interface IApiCompletionClient
{
    Task<ApiCompletionResult> CompleteAsync(
        ApiCompletionRequest request,
        CancellationToken cancellationToken);
}

public interface IPlanningDraftRepository
{
    Task<bool> ProjectEnabledAsync(Guid projectId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<PlanningDraftView> Items, int Total)> ListAsync(
        string? status,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    Task<PlanningDraft?> GetTrackedAsync(Guid draftId, CancellationToken cancellationToken);

    Task<PlanningDraftView?> GetViewAsync(Guid draftId, CancellationToken cancellationToken);

    Task<bool> TryDecideAsync(
        Guid draftId,
        PlanningDraftStatus status,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task AddAsync(PlanningDraft draft, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}
