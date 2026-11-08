using System.Reflection;

namespace AgentOS.Api;

public sealed record VersionInfo(string Version, string GitCommit, string BuildTimeUtc, string Environment);

public static class BuildMetadata
{
    private static BuildInfo? ReadBuildInfo(Assembly assembly)
    {
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? string.Empty, "build-info.json");
            return File.Exists(path)
                ? System.Text.Json.JsonSerializer.Deserialize<BuildInfo>(File.ReadAllText(path))
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed record BuildInfo(string? Commit, string? BuildTimeUtc);

    public static VersionInfo Get(string? environmentName = null)
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "Unknown";
        var buildInfo = ReadBuildInfo(assembly);
        var commit = buildInfo?.Commit
            ?? Environment.GetEnvironmentVariable("GITHUB_SHA")
            ?? Environment.GetEnvironmentVariable("AGENTOS_GIT_COMMIT")
            ?? "Unknown";
        var buildTime = buildInfo?.BuildTimeUtc
            ?? Environment.GetEnvironmentVariable("AGENTOS_BUILD_TIME");
        if (string.IsNullOrWhiteSpace(buildTime))
        {
            try
            {
                var location = assembly.Location;
                buildTime = File.Exists(location)
                    ? File.GetLastWriteTimeUtc(location).ToString("O")
                    : DateTime.UtcNow.ToString("O");
            }
            catch (Exception)
            {
                buildTime = DateTime.UtcNow.ToString("O");
            }
        }

        var environment = environmentName
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";
        return new VersionInfo(version, commit, buildTime, environment);
    }
}
