namespace AgentOS.AgentAdapters;

/// <summary>
/// Contract for agent execution adapters. The first concrete adapter (Codex CLI) is
/// implemented in AOS-007; AOS-001 only establishes the seam so the platform is not
/// hard-wired to a single CLI.
/// </summary>
public interface IAgentAdapter
{
    /// <summary>Stable adapter code, for example <c>codex-cli</c>.</summary>
    string Code { get; }
}
