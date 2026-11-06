using AgentOS.AgentAdapters;
using AgentOS.AgentAdapters.Codex;
using AgentOS.Application.Tasks;
using AgentOS.Infrastructure.Skills;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Tests;

public sealed class SkillContentReaderTests : IDisposable
{
    private readonly string _root;

    public SkillContentReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "agentos-skills", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Reads_a_skill_under_the_root()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "bugfix.md"), "1. Reproduce");

        var content = await CreateReader().ReadAsync("bugfix.md", CancellationToken.None);

        Assert.Equal("1. Reproduce", content);
    }

    [Fact]
    public async Task Rejects_path_traversal()
    {
        var content = await CreateReader().ReadAsync("../outside.md", CancellationToken.None);

        Assert.Null(content);
    }

    [Fact]
    public async Task Rejects_oversized_skills()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "big.md"), new string('x', 200));

        var reader = new SkillContentReader(
            Options.Create(new SkillContentOptions { RootPath = _root, MaxBytes = 100 }),
            NullLogger<SkillContentReader>.Instance);

        Assert.Null(await reader.ReadAsync("big.md", CancellationToken.None));
    }

    [Fact]
    public async Task Missing_root_or_file_returns_null()
    {
        Assert.Null(await CreateReader().ReadAsync("missing.md", CancellationToken.None));

        var unconfigured = new SkillContentReader(
            Options.Create(new SkillContentOptions()),
            NullLogger<SkillContentReader>.Instance);
        Assert.Null(await unconfigured.ReadAsync("bugfix.md", CancellationToken.None));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private SkillContentReader CreateReader() => new(
        Options.Create(new SkillContentOptions { RootPath = _root }),
        NullLogger<SkillContentReader>.Instance);
}

public sealed class CodexCliSmokeTests
{
    [SkippableFact]
    public async Task Real_codex_executes_a_read_only_prompt()
    {
        Skip.If(
            Environment.GetEnvironmentVariable("AGENTOS_CODEX_SMOKE") != "1",
            "Set AGENTOS_CODEX_SMOKE=1 to run the real Codex CLI smoke test.");

        var executable = ResolveCodexExecutable();
        var workspace = Path.Combine(Path.GetTempPath(), "agentos-codex-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        var adapter = new CodexCliAdapter(
            Options.Create(new CodexCliOptions
            {
                ExecutablePath = executable,
                SandboxMode = "read-only",
                TimeoutSeconds = 240,
                EnvironmentAllowList =
                [
                    "PATH", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "TEMP", "TMP",
                    "SYSTEMROOT", "WINDIR", "COMSPEC", "PATHEXT",
                    "HTTPS_PROXY", "HTTP_PROXY", "NO_PROXY", "OPENAI_API_KEY", "CODEX_HOME"
                ]
            }),
            new AgentAdapters.Processes.ExternalProcessRunner(TimeProvider.System),
            TimeProvider.System,
            NullLogger<CodexCliAdapter>.Instance);

        var result = await adapter.ExecuteAsync(
            new AgentExecutionRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "smoke",
                workspace,
                "Reply with exactly: ADAPTER_OK. Do not run any tools.",
                180,
                new AgentExecutionPolicy(AgentSandboxMode.ReadOnly, [])),
            CancellationToken.None);

        Assert.Equal(AgentExecutionStatus.Succeeded, result.Status);
        Assert.Contains("ADAPTER_OK", result.Summary);
    }

    private static string ResolveCodexExecutable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "codex";
        }

        var binRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenAI",
            "Codex",
            "bin");

        if (Directory.Exists(binRoot))
        {
            var executable = Directory
                .EnumerateFiles(binRoot, "codex.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (executable is not null)
            {
                return executable;
            }
        }

        return "codex.exe";
    }
}
