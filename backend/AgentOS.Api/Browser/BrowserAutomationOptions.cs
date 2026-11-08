namespace AgentOS.Api.Browser;

public sealed class BrowserAutomationOptions
{
    public bool Enabled { get; set; }
    public bool Headless { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 30;
    public string[] AllowedHosts { get; set; } = [];
    public string ScreenshotDirectory { get; set; } = "browser-screenshots";
    public int MaxConcurrentSessions { get; set; } = 2;
    public int ScreenshotRetentionHours { get; set; } = 24;
}

public sealed class BrowserActionRequest
{
    public string? Action { get; init; }
    public string? Url { get; init; }
    public string? Selector { get; init; }
}
