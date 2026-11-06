using AgentOS.Runner;
using AgentOS.Runner.Handlers;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RunnerOptions>(builder.Configuration.GetSection("Runner"));

builder.Services.AddHttpClient<IRunnerApi, RunnerApiClient>((services, httpClient) =>
{
    var options = services.GetRequiredService<IOptions<RunnerOptions>>().Value;
    httpClient.BaseAddress = new Uri(options.BaseUrl);
    httpClient.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
});

builder.Services.AddRunnerServices();
builder.Services.AddHostedService<RunnerService>();

var host = builder.Build();
host.Run();
