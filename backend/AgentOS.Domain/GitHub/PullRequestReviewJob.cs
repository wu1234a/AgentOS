namespace AgentOS.Domain.GitHub;

public sealed class PullRequestReviewJob
{
    public Guid Id { get; init; }
    public string Repository { get; init; } = string.Empty;
    public int PullRequestNumber { get; init; }
    public string HeadSha { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string DeliveryId { get; init; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }
    public long? GitHubReviewId { get; set; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
