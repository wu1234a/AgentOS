using System.Net;
using System.Text;
using AgentOS.AgentAdapters;

namespace AgentOS.Api.Tests;

public sealed class AnthropicAgentAdapterTests
{
    [Fact]
    public async Task Executes_anthropic_tool_use_loop()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"agentos-anthropic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        Environment.SetEnvironmentVariable("AGENTOS_TEST_ANTHROPIC_KEY", "test-key");
        try
        {
            var handler = new SequenceHandler(
                """
                {"content":[{"type":"tool_use","id":"tool-1","name":"write_file","input":{"path":"hello.txt","content":"hello"}}]}
                """,
                """
                {"content":[{"type":"text","text":"done"}]}
                """);
            var adapter = new AnthropicAgentAdapter(new HttpClient(handler));

            var result = await adapter.ExecuteAsync(new AgentExecutionRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "agentos",
                workspace,
                "write hello",
                60,
                new AgentExecutionPolicy(AgentSandboxMode.WorkspaceWrite, []),
                Model: "claude-test",
                Provider: "anthropic",
                ConfigurationJson: "{\"baseUrl\":\"https://fake.local\"}",
                SecretReference: "AGENTOS_TEST_ANTHROPIC_KEY"), CancellationToken.None);

            Assert.Equal(AgentExecutionStatus.Succeeded, result.Status);
            Assert.Equal("done", result.Summary);
            Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(workspace, "hello.txt")));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private sealed class SequenceHandler(params string[] responses) : HttpMessageHandler
    {
        private int _requests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(_requests++, responses.Length - 1);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses[index], Encoding.UTF8, "application/json")
            });
        }
    }
}
