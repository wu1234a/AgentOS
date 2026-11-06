using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AgentOS.Application.Planning;
using AgentOS.Infrastructure.Planning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentOS.Api.Tests;

public sealed class ApiCompletionClientTests
{
    [Fact]
    public async Task Sends_an_openai_compatible_request_and_parses_content()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var handler = new StubHandler(async (request, cancellationToken) =>
        {
            captured = request;
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return JsonResponse("""{"choices":[{"message":{"content":"# AOS-099 draft"}}]}""");
        });

        var result = await CreateClient(handler).CompleteAsync(Request(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("# AOS-099 draft", result.Content);
        Assert.NotNull(captured);
        Assert.Equal("https://api.moonshot.cn/v1/chat/completions", captured!.RequestUri!.ToString());
        Assert.Equal("Bearer test-key", captured.Headers.Authorization!.ToString());
        Assert.Contains("kimi-k2", capturedBody);
        Assert.Contains("system", capturedBody);
        Assert.Contains("user", capturedBody);
        Assert.Contains("max_completion_tokens", capturedBody);
        Assert.DoesNotContain("\"max_tokens\"", capturedBody);
    }

    [Fact]
    public async Task Unset_optional_parameters_are_omitted_from_the_request()
    {
        string? body = null;
        var handler = new StubHandler(async (request, cancellationToken) =>
        {
            body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return JsonResponse("""{"choices":[{"message":{"content":"ok"}}]}""");
        });

        await CreateClient(handler).CompleteAsync(
            Request() with { Temperature = null, MaxTokens = null },
            CancellationToken.None);

        Assert.DoesNotContain("temperature", body);
        Assert.DoesNotContain("max_completion_tokens", body);
        Assert.DoesNotContain("max_tokens", body);
    }

    [Fact]
    public async Task Rejects_hosts_outside_the_allowlist()
    {
        var called = false;
        var handler = new StubHandler((_, _) =>
        {
            called = true;
            return Task.FromResult(JsonResponse("{}"));
        });

        var result = await CreateClient(handler)
            .CompleteAsync(Request() with { BaseUrl = "https://evil.example/v1" }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("planner-host-not-allowed", result.ErrorCode);
        Assert.False(called);
    }

    [Fact]
    public async Task Provider_errors_and_invalid_responses_are_reported()
    {
        var httpError = await CreateClient(new StubHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent(
                        """{"error":{"message":"invalid temperature"}}""",
                        Encoding.UTF8,
                        "application/json")
                })))
            .CompleteAsync(Request(), CancellationToken.None);
        Assert.Equal("planner-provider-http", httpError.ErrorCode);
        Assert.Contains("invalid temperature", httpError.ErrorMessage);

        var invalid = await CreateClient(new StubHandler((_, _) =>
                Task.FromResult(JsonResponse("""{"choices":[]}"""))))
            .CompleteAsync(Request(), CancellationToken.None);
        Assert.Equal("planner-invalid-response", invalid.ErrorCode);
    }

    [Fact]
    public async Task Malformed_choice_structures_are_reported_as_provider_errors()
    {
        var nullChoices = await CreateClient(new StubHandler((_, _) =>
                Task.FromResult(JsonResponse("""{"choices":null}"""))))
            .CompleteAsync(Request(), CancellationToken.None);
        Assert.Equal("planner-invalid-response", nullChoices.ErrorCode);

        var nullChoice = await CreateClient(new StubHandler((_, _) =>
                Task.FromResult(JsonResponse("""{"choices":[null]}"""))))
            .CompleteAsync(Request(), CancellationToken.None);
        Assert.Equal("planner-invalid-response", nullChoice.ErrorCode);
    }

    [Fact]
    public async Task Timeout_is_reported_as_a_timeout()
    {
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return JsonResponse("{}");
        });

        var result = await CreateClient(handler)
            .CompleteAsync(Request() with { TimeoutSeconds = 1 }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("planner-timeout", result.ErrorCode);
    }

    private static ApiCompletionRequest Request() => new(
        "https://api.moonshot.cn/v1",
        "test-key",
        "kimi-k2",
        "system prompt",
        "user prompt",
        0.2,
        1024,
        30,
        "kimi");

    private static ApiCompletionClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new ApiCompletionClient(
            new StubFactory(httpClient),
            Options.Create(new PlannerOptions { AllowedProviderHosts = ["api.moonshot.cn", "api.openai.com"] }),
            NullLogger<ApiCompletionClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
