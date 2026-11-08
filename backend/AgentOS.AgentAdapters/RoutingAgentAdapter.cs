namespace AgentOS.AgentAdapters;

/// <summary>Routes an execution request to the Codex CLI or the generic OpenAI-compatible adapter.</summary>
public sealed class RoutingAgentAdapter(
    IAgentAdapter codexCli,
    IAgentAdapter openAiCompatible,
    IAgentAdapter anthropic) : IAgentAdapter
{
    public string Code => "routing";

    public Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Provider)
            || string.Equals(request.Provider, "codex-cli", StringComparison.OrdinalIgnoreCase))
        {
            return codexCli.ExecuteAsync(request, cancellationToken);
        }

        if (string.Equals(request.Provider, "anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return anthropic.ExecuteAsync(request, cancellationToken);
        }

        return openAiCompatible.ExecuteAsync(request, cancellationToken);
    }
}
