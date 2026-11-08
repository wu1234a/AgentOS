namespace AgentOS.Api.Knowledge;

public sealed class KnowledgeOptions
{
    public EmbeddingOptions Embedding { get; set; } = new();
}

public sealed class EmbeddingOptions
{
    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "text-embedding-3-small";
}
