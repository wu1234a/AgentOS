using System.Text.Json;
using System.Text.RegularExpressions;
using AgentOS.AgentAdapters.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOS.AgentAdapters.Codex;

/// <summary>
/// Codex CLI adapter configuration. The executable and argument profile come from trusted
/// runner configuration; task data only ever reaches the CLI through stdin as prompt text.
/// </summary>
public sealed class CodexCliOptions
{
    public string ExecutablePath { get; set; } = "codex";

    /// <summary>Either <c>read-only</c> or <c>workspace-write</c>. Danger modes are rejected.</summary>
    public string SandboxMode { get; set; } = "read-only";

    public string? Model { get; set; }

    public int TimeoutSeconds { get; set; } = 900;

    public int MaxOutputBytes { get; set; } = 2_000_000;

    /// <summary>Verified CLI version range, recorded in the delivery report (informational).</summary>
    public string CliVersionConstraint { get; set; } = ">=0.166.0";

    public string[] EnvironmentAllowList { get; set; } =
    [
        "PATH", "PATHEXT", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "TEMP", "TMP",
        "SYSTEMROOT", "WINDIR", "COMSPEC", "LANG", "LC_ALL", "TERM",
        "HTTPS_PROXY", "HTTP_PROXY", "NO_PROXY", "ALL_PROXY",
        "OPENAI_API_KEY", "CODEX_HOME", "SSL_CERT_FILE"
    ];

    /// <summary>Environment variables whose values are redacted from captured output.</summary>
    public string[] RedactEnvironmentVariables { get; set; } = ["OPENAI_API_KEY"];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ExecutablePath))
        {
            errors.Add("Codex:ExecutablePath is required.");
        }

        if (OperatingSystem.IsWindows()
            && (ExecutablePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                || ExecutablePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("Codex:ExecutablePath must point at codex.exe on Windows; .cmd/.bat shims require a shell.");
        }

        if (!TryParseSandbox(SandboxMode, out _))
        {
            errors.Add("Codex:SandboxMode must be read-only or workspace-write.");
        }

        if (TimeoutSeconds is < 1 or > 7200)
        {
            errors.Add("Codex:TimeoutSeconds must be between 1 and 7200.");
        }

        if (MaxOutputBytes is < 4096 or > 20_000_000)
        {
            errors.Add("Codex:MaxOutputBytes must be between 4096 and 20000000.");
        }

        return errors;
    }

    public static bool TryParseSandbox(string? value, out AgentSandboxMode mode)
    {
        mode = AgentSandboxMode.ReadOnly;

        if (string.Equals(value, "read-only", StringComparison.OrdinalIgnoreCase))
        {
            mode = AgentSandboxMode.ReadOnly;
            return true;
        }

        if (string.Equals(value, "workspace-write", StringComparison.OrdinalIgnoreCase))
        {
            mode = AgentSandboxMode.WorkspaceWrite;
            return true;
        }

        return false;
    }
}

/// <summary>
/// Runs <c>codex exec</c> non-interactively inside the validated working directory.
/// Verified against codex-cli 0.166.0-alpha.6 (see docs/reports/AOS-007-DELIVERY-REPORT.md).
/// </summary>
public sealed class CodexCliAdapter(
    IOptions<CodexCliOptions> options,
    IProcessRunner processRunner,
    TimeProvider timeProvider,
    ILogger<CodexCliAdapter> logger) : IAgentAdapter
{
    public string Code => "codex-cli";

    public async Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        var errors = configuration.Validate();
        var startedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (errors.Count > 0)
        {
            return Unavailable(startedAt, "codex-config-invalid", string.Join(" ", errors));
        }

        if (!CodexCliOptions.TryParseSandbox(configuration.SandboxMode, out var sandbox))
        {
            return Unavailable(startedAt, "codex-sandbox-invalid", "Unsupported sandbox mode.");
        }

        if (LooksLikePath(configuration.ExecutablePath) && !File.Exists(configuration.ExecutablePath))
        {
            return Unavailable(
                startedAt,
                "codex-not-found",
                $"Codex CLI was not found at '{configuration.ExecutablePath}'.");
        }

        var artifactDirectory = Path.Combine(
            Path.GetTempPath(),
            "agentos",
            "codex",
            request.TaskId.ToString("N"),
            request.AttemptId.ToString("N"));
        Directory.CreateDirectory(artifactDirectory);
        var lastMessagePath = Path.Combine(artifactDirectory, "last-message.txt");

        var arguments = new List<string>
        {
            "exec",
            "--sandbox",
            sandbox == AgentSandboxMode.ReadOnly ? "read-only" : "workspace-write",
            "-C",
            request.WorkingDirectory,
            "--skip-git-repo-check",
            "--ephemeral",
            "--color",
            "never",
            "--json",
            "-o",
            lastMessagePath
        };

        if (!string.IsNullOrWhiteSpace(configuration.Model))
        {
            arguments.Add("-m");
            arguments.Add(configuration.Model);
        }

        arguments.Add("-");

        var redactions = ReadRedactions(configuration);
        var onOutput = request.OnLog is null
            ? null
            : new Action<string, string>((stream, line) =>
            {
                request.OnLog(stream == "stderr" ? "warn" : "info", LogRedactor.Redact(line, redactions));
            });

        var timeoutSeconds = Math.Clamp(
            Math.Min(request.TimeoutSeconds, configuration.TimeoutSeconds),
            1,
            7200);

        ProcessRunResult run;

        try
        {
            run = await processRunner.RunAsync(
                new ProcessRunRequest(
                    configuration.ExecutablePath,
                    arguments,
                    request.WorkingDirectory,
                    request.Prompt,
                    TimeSpan.FromSeconds(timeoutSeconds),
                    BuildEnvironment(configuration),
                    configuration.MaxOutputBytes,
                    artifactDirectory,
                    redactions,
                    onOutput),
                cancellationToken);
        }
        catch (ProcessStartException exception)
        {
            logger.LogError(exception, "Could not start the Codex CLI for task {TaskId}.", request.TaskId);
            return Unavailable(startedAt, "codex-unavailable", exception.Message);
        }

        var finishedAt = timeProvider.GetUtcNow().UtcDateTime;
        var sessionId = ExtractSessionId(run.Stdout);
        var summary = ReadSummary(lastMessagePath, run.Stdout, redactions);

        if (summary is not null && File.Exists(lastMessagePath))
        {
            // Codex writes -o verbatim; replace the artifact with the redacted summary so the
            // raw file cannot retain a credential.
            try
            {
                await File.WriteAllTextAsync(lastMessagePath, summary);
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "Could not rewrite the redacted Codex summary artifact.");
            }
        }

        if (run.TimedOut)
        {
            return Result(
                AgentExecutionStatus.TimedOut,
                null,
                startedAt,
                finishedAt,
                run,
                summary,
                "codex-timeout",
                $"Codex did not finish within {timeoutSeconds}s.",
                sessionId);
        }

        if (run.Cancelled)
        {
            return Result(
                AgentExecutionStatus.Cancelled,
                null,
                startedAt,
                finishedAt,
                run,
                summary,
                "codex-cancelled",
                "Execution was cancelled.",
                sessionId);
        }

        if (run.ExitCode != 0)
        {
            return Result(
                AgentExecutionStatus.Failed,
                run.ExitCode,
                startedAt,
                finishedAt,
                run,
                summary,
                $"codex-exit-{run.ExitCode}",
                $"Codex exited with code {run.ExitCode}.",
                sessionId);
        }

        return Result(
            AgentExecutionStatus.Succeeded,
            run.ExitCode,
            startedAt,
            finishedAt,
            run,
            summary,
            null,
            null,
            sessionId);
    }

    private static bool LooksLikePath(string executable)
        => executable.Contains(Path.DirectorySeparatorChar)
           || executable.Contains(Path.AltDirectorySeparatorChar);

    private static Dictionary<string, string> BuildEnvironment(CodexCliOptions configuration)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in configuration.EnvironmentAllowList)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                environment[name] = value;
            }
        }

        environment["NO_COLOR"] = "1";
        return environment;
    }

    private static List<string> ReadRedactions(CodexCliOptions configuration)
    {
        var secrets = new List<string>();

        foreach (var name in configuration.RedactEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                secrets.Add(value);
            }
        }

        return secrets;
    }

    private static string? ExtractSessionId(string stdout)
    {
        var match = Regex.Match(stdout, "\"thread_id\"\\s*:\\s*\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ReadSummary(string lastMessagePath, string stdout, IReadOnlyList<string> redactions)
    {
        if (File.Exists(lastMessagePath))
        {
            try
            {
                var text = File.ReadAllText(lastMessagePath);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return LogRedactor.Redact(text.Trim(), redactions);
                }
            }
            catch (IOException)
            {
                // Fall back to the JSON event stream.
            }
        }

        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("item", out var item)
                    && item.TryGetProperty("type", out var type)
                    && type.GetString() == "agent_message"
                    && item.TryGetProperty("text", out var text))
                {
                    return LogRedactor.Redact(text.GetString(), redactions);
                }
            }
            catch (JsonException)
            {
                // Not a JSON event line.
            }
        }

        return null;
    }

    private static AgentExecutionResult Result(
        AgentExecutionStatus status,
        int? exitCode,
        DateTime startedAt,
        DateTime finishedAt,
        ProcessRunResult run,
        string? summary,
        string? errorCode,
        string? errorMessage,
        string? sessionId)
        => new(
            status,
            exitCode,
            startedAt,
            finishedAt,
            run.StdoutPath,
            run.StderrPath,
            summary,
            errorCode,
            errorMessage,
            sessionId);

    private static AgentExecutionResult Unavailable(
        DateTime startedAt,
        string errorCode,
        string errorMessage)
        => new(
            AgentExecutionStatus.Unavailable,
            null,
            startedAt,
            startedAt,
            null,
            null,
            null,
            errorCode,
            errorMessage,
            null);
}
