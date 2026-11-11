namespace AgentOS.Api.Backlog;

public sealed class BacklogOptions
{
    public bool Enabled { get; set; }

    public int PollSeconds { get; set; } = 15;
}
