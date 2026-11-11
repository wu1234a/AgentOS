namespace AgentOS.Api.Backlog;

public sealed class BacklogOptions
{
    public bool Enabled { get; set; }

    public int PollSeconds { get; set; } = 15;

    /// <summary>Maximum repair items generated for one failed backlog task.</summary>
    public int MaxRepairAttempts { get; set; } = 1;
}
