namespace AgentOS.Domain.Knowledge;

public sealed class KnowledgeDocument
{
    public Guid Id { get; init; }
    public Guid? ProjectId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string TagsJson { get; init; } = "[]";
    public string Source { get; init; } = "manual";
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
}
