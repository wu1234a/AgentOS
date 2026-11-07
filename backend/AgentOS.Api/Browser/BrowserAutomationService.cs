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

        cancellationToken.ThrowIfCancellationRequested();
        var action = request.Action?.Trim().ToLowerInvariant() ?? "extracttext";
        try
        {
            return await ExecuteBrowserAsync(configuration, uri, action, request.Selector, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return (null, "browser-action-cancelled");
        }
        catch (Exception)
        {
            return (null, "browser-action-failed");
        }
    }

    private async Task<(BrowserActionResult? Result, string? Error)> ExecuteBrowserAsync(
        BrowserAutomationOptions configuration,
        Uri uri,
        string action,
        string? selector,
        CancellationToken cancellationToken)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = configuration.Headless
        });
        await using var registration = cancellationToken.Register(() => _ = browser.CloseAsync());
        var page = await browser.NewPageAsync();
        page.SetDefaultTimeout(configuration.TimeoutSeconds * 1000);
        await page.RouteAsync("**/*", route =>
        {
            if (Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var target)
                && (target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps)
                && configuration.AllowedHosts.Contains(target.Host, StringComparer.OrdinalIgnoreCase))
            {
                return route.ContinueAsync();
            }

            return route.AbortAsync();
        });
        await page.GotoAsync(uri.ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        cancellationToken.ThrowIfCancellationRequested();

        if (action == "click")
        {
            if (string.IsNullOrWhiteSpace(selector))
            {
                return (null, "selector-required");
            }

            await page.ClickAsync(selector);
            return (new BrowserActionResult("click", null, null), null);
        }

        if (action == "screenshot")
        {
            Directory.CreateDirectory(configuration.ScreenshotDirectory);
            var path = Path.Combine(configuration.ScreenshotDirectory, $"browser-{Guid.NewGuid():N}.png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
            return (new BrowserActionResult("screenshot", null, path), null);
        }

        var text = string.IsNullOrWhiteSpace(selector)
            ? await page.InnerTextAsync("body")
            : await page.InnerTextAsync(selector);
        logger.LogInformation("Browser action {Action} completed for host {Host}.", action, uri.Host);
        return (new BrowserActionResult("extracttext", text.Length <= 20000 ? text : text[..20000], null), null);
    }
}
