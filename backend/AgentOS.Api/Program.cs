using AgentOS.Api.Endpoints;
using AgentOS.Api.Reviews;
using AgentOS.Api.Filters;
using AgentOS.Api.Realtime;
using AgentOS.Api.Health;
using AgentOS.Api.Security;
using AgentOS.Application.Events;
using AgentOS.Application.Agents;
using AgentOS.AgentAdapters;
using AgentOS.AgentAdapters.Codex;
using AgentOS.AgentAdapters.Processes;
using AgentOS.Application.CodingRuns;
using AgentOS.Application.ModelProfiles;
using AgentOS.Application.Observability;
using AgentOS.Application.Planning;
using AgentOS.Application.Reviews;
using AgentOS.Application.Qa;
using AgentOS.Application.Rules;
using AgentOS.Application.Skills;
using AgentOS.Application.Runners;
using AgentOS.Application.Security;
using AgentOS.Application.Tasks;
using AgentOS.Infrastructure;
using AgentOS.Infrastructure.Health;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

builder.Services.AddProblemDetails();
builder.Services.AddSignalR();
builder.Services.AddAgentOSInfrastructure(builder.Configuration);

builder.Services.AddSingleton<CreateEventValidator>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<RuleValidator>();
builder.Services.AddScoped<RuleService>();
builder.Services.AddScoped<ITaskDispatcher, TaskDispatchService>();
builder.Services.Configure<TaskQueueOptions>(builder.Configuration.GetSection("TaskQueue"));
builder.Services.Configure<RunnerRegistrationOptions>(builder.Configuration.GetSection("Runner"));
builder.Services.Configure<SkillContentOptions>(builder.Configuration.GetSection("Skills"));
builder.Services.Configure<ArtifactOptions>(builder.Configuration.GetSection("Artifacts"));
builder.Services.Configure<PlannerOptions>(builder.Configuration.GetSection("Planner"));
builder.Services.Configure<CodexCliOptions>(builder.Configuration.GetSection("Codex"));
builder.Services.AddSingleton<IProcessRunner, ExternalProcessRunner>();
builder.Services.AddSingleton<IAgentAdapter, CodexCliAdapter>();
builder.Services.AddHttpClient("planner", client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<TaskQueueService>();
builder.Services.AddScoped<RunnerRegistrationService>();
builder.Services.AddScoped<AgentService>();
builder.Services.AddScoped<ModelProfileService>();
builder.Services.AddScoped<SkillService>();
builder.Services.AddScoped<PlannerService>();
builder.Services.AddScoped<CodingRunService>();
builder.Services.AddScoped<SubtaskService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<QaService>();
builder.Services.AddScoped<IReviewAgent, CodexReviewAgent>();
builder.Services.AddScoped<ApiClientAuthenticationFilter>();
builder.Services.AddScoped<RunnerAuthenticationFilter>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IApiClientAuthenticator, DevelopmentApiKeyAuthenticator>();
}
else
{
    builder.Services.AddSingleton<IApiClientAuthenticator, UnconfiguredApiClientAuthenticator>();
}

builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sqlserver", tags: ["ready"]);

builder.Services.AddCors(options => options.AddPolicy("web-dev", policy => policy
    .WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var maxRequestBodyBytes = builder.Configuration.GetValue("Events:MaxRequestBodyBytes", 256 * 1024);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxRequestBodyBytes);

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError
});
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

if (app.Environment.IsDevelopment())
{
    app.UseCors("web-dev");
}

app.UseSerilogRequestLogging();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.MapEventEndpoints();
app.MapRuleEndpoints();
app.MapRunnerEndpoints();
app.MapRunnerRegistrationEndpoints();
app.MapTaskEndpoints();
app.MapObservabilityEndpoints();
app.MapAgentEndpoints();
app.MapModelProfileEndpoints();
app.MapSkillEndpoints();
app.MapPlannerEndpoints();
app.MapCodingRunEndpoints();
app.MapSubtaskEndpoints();
app.MapHub<TaskHub>("/hubs/tasks");

app.MapGet("/", () => Results.Ok(new
{
    service = "AgentOS.Api",
    status = "ok",
    timestampUtc = DateTime.UtcNow
}));

app.Run();

/// <summary>Exposed so the integration test host can boot the real application pipeline.</summary>
public partial class Program;
