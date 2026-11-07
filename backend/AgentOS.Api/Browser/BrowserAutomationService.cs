using Microsoft.Playwright;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Browser;

public sealed record BrowserActionResult(string Action, string? Text, string? ScreenshotPath);

public sealed class BrowserAutomationService(
    IOptions<BrowserAutomationOptions> options,
    ILogger<BrowserAutomationService> logger)
{
    public async Task<(BrowserActionResult? Result, string? Error)> ExecuteAsync(
        BrowserActionRequest request,
        CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        if (!configuration.Enabled)
        {
            return (null, "browser-automation-disabled");
        }

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return (null, "invalid-url");
        }

        if (configuration.AllowedHosts.Length == 0
            || !configuration.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            return (null, "host-not-allowed");
        }

        var action = request.Action?.Trim().ToLowerInvariant() ?? "extracttext";
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = configuration.Headless
        });
        var page = await browser.NewPageAsync();
        page.SetDefaultTimeout(configuration.TimeoutSeconds * 1000);
        await page.GotoAsync(uri.ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        if (action == "click")
        {
            if (string.IsNullOrWhiteSpace(request.Selector))
            {
                return (null, "selector-required");
            }

            await page.ClickAsync(request.Selector);
            return (new BrowserActionResult("click", null, null), null);
        }

        if (action == "screenshot")
        {
            Directory.CreateDirectory(configuration.ScreenshotDirectory);
            var path = Path.Combine(configuration.ScreenshotDirectory, $"browser-{Guid.NewGuid():N}.png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
            return (new BrowserActionResult("screenshot", null, path), null);
        }

        var text = string.IsNullOrWhiteSpace(request.Selector)
            ? await page.InnerTextAsync("body")
            : await page.InnerTextAsync(request.Selector);
        logger.LogInformation("Browser action {Action} completed for host {Host}.", action, uri.Host);
        return (new BrowserActionResult("extracttext", text.Length <= 20000 ? text : text[..20000], null), null);
    }
}
