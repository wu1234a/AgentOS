using System.Net;
using System.Text;
using AgentOS.AgentAdapters;

namespace AgentOS.Api.Tests;

public sealed class OpenAiCompatibleAgentAdapterTests
{
    [Fact]
    public async Task Executes_tool_loop_and_writes_workspace_file()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"agentos-adapter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        Environment.SetEnvironmentVariable("AGENTOS_TEST_PROVIDER_KEY", "test-key");
        try
        {
            var handler = new SequenceHandler(
                """
                {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call-1","type":"function","function":{"name":"write_file","arguments":"{\"path\":\"hello.txt\",\"content\":\"hello\"}"}}]}}]}
                """,
                """
                {"choices":[{"message":{"role":"assistant","content":"done"}}]}
                """);
            var adapter = new OpenAiCompatibleAgentAdapter(new HttpClient(handler));

            var result = await adapter.ExecuteAsync(new AgentExecutionRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "agentos",
                workspace,
                "write hello",
                60,
                new AgentExecutionPolicy(AgentSandboxMode.WorkspaceWrite, []),
                Model: "test-model",
                Provider: "openai",
                ConfigurationJson: "{\"baseUrl\":\"https://fake.local/v1\"}",
                SecretReference: "AGENTOS_TEST_PROVIDER_KEY"), CancellationToken.None);

            Assert.Equal(AgentExecutionStatus.Succeeded, result.Status);
            Assert.Equal("done", result.Summary);
            Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(workspace, "hello.txt")));
            Assert.Equal(2, handler.Requests);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_paths_outside_workspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"agentos-adapter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        Environment.SetEnvironmentVariable("AGENTOS_TEST_PROVIDER_KEY", "test-key");
        try
        {
            var handler = new SequenceHandler(
                """
                {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call-1","type":"function","function":{"name":"read_file","arguments":"{\"path\":\"../outside.txt\"}"}}]}}]}
                """,
                """
                {"choices":[{"message":{"role":"assistant","content":"blocked"}}]}
                """);
            var adapter = new OpenAiCompatibleAgentAdapter(new HttpClient(handler));

            var result = await adapter.ExecuteAsync(new AgentExecutionRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "agentos",
                workspace,
                "read outside",
                60,
                new AgentExecutionPolicy(AgentSandboxMode.ReadOnly, []),
                Model: "test-model",
                Provider: "openai",
                ConfigurationJson: "{\"baseUrl\":\"https://fake.local/v1\"}",
                SecretReference: "AGENTOS_TEST_PROVIDER_KEY"), CancellationToken.None);

            Assert.Equal(AgentExecutionStatus.Succeeded, result.Status);
            Assert.Equal("blocked", result.Summary);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private sealed class SequenceHandler(params string[] responses) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(Requests, responses.Length - 1);
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses[index], Encoding.UTF8, "application/json")
            });
        }
    }
}
