using AgentOS.Api.Browser;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Tests;

public sealed class BrowserAutomationTests
{
    [Fact]
    public async Task Disabled_adapter_returns_configuration_error()
    {
        var service = new BrowserAutomationService(
            Options.Create(new BrowserAutomationOptions { Enabled = false }),
            NullLogger<BrowserAutomationService>.Instance);

        var (result, error) = await service.ExecuteAsync(
            new BrowserActionRequest { Action = "extracttext", Url = "https://example.com" },
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal("browser-automation-disabled", error);
    }

    [Fact]
    public async Task Rejects_hosts_outside_the_allowlist()
    {
        var service = new BrowserAutomationService(
            Options.Create(new BrowserAutomationOptions { Enabled = true, AllowedHosts = ["allowed.example"] }),
            NullLogger<BrowserAutomationService>.Instance);

        var (result, error) = await service.ExecuteAsync(
            new BrowserActionRequest { Action = "extracttext", Url = "https://example.com" },
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal("host-not-allowed", error);
    }
}
