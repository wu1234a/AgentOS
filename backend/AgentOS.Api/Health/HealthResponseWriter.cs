using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentOS.Api.Health;

/// <summary>
/// Writes a minimal JSON health response. Exception details, connection strings, and
/// configuration values are deliberately never included.
/// </summary>
public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries
                .Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString()
                })
                .ToArray(),
            durationMs = (long)report.TotalDuration.TotalMilliseconds
        };

        return context.Response.WriteAsJsonAsync(payload);
    }
}
