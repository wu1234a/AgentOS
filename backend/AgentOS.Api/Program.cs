using AgentOS.Api.Backlog;
using AgentOS.Api.Endpoints;
using AgentOS.Api.Reviews;
using AgentOS.Api.GitHub;
using AgentOS.Api.Browser;
using AgentOS.Api.Business;
using AgentOS.Api.Knowledge;
using AgentOS.Api.Operations;
using AgentOS.Api.Pipelines;
using AgentOS.Api.Scheduling;
using AgentOS.Api.Usage;
using AgentOS.Api.Workers;
using AgentOS.Api.Telegram;
using AgentOS.Api.Filters;
using AgentOS.Api.Realtime;
using AgentOS.Api.Health;
using AgentOS.Api.Security;
using AgentOS.Application.Events;
using AgentOS.Application.Agents;
using AgentOS.AgentAdapters;
using AgentOS.AgentAdapters.Codex;
using AgentOS.AgentAdapters.Processes;
using AgentOS.Application.Approvals;
using AgentOS.Application.Decisions;
using AgentOS.Application.CodingRuns;
using AgentOS.Application.ModelProfiles;
using AgentOS.Application.Observability;
using AgentOS.Application.Planning;
using AgentOS.Application.Projects;
using AgentOS.Application.Scheduling;
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
builder.Services.Configure<BacklogOptions>(builder.Configuration.GetSection("Backlog"));
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection("Telegram"));
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection("GitHub"));
builder.Services.AddHttpClient<IGitHubPullRequestClient, GitHubPullRequestClient>();
builder.Services.AddHttpClient<IGitHubCommitStatusClient, GitHubCommitStatusClient>();
builder.Services.AddScoped<QualityGateService>();
builder.Services.AddScoped<MergeReadinessService>();
builder.Services.AddScoped<IGitHubReviewQueue, GitHubReviewQueue>();
builder.Services.AddHostedService<GitHubReviewWorker>();
builder.Services.AddHostedService<GitHubAutoMergeWorker>();
builder.Services.AddHostedService<BacklogWorker>();
builder.Services.AddHostedService<ScheduledJobWorker>();
builder.Services.AddHostedService<DeadLetterRecoveryWorker>();
builder.Services.AddHttpClient<ITelegramClient, HttpTelegramClient>()
    .RemoveAllLoggers();
builder.Services.AddScoped<TelegramUpdateProcessor>();
builder.Services.AddScoped<ITelegramRejectSessionStore, TelegramRejectSessionStore>();
builder.Services.AddScoped<TelegramOutbox>();
builder.Services.AddScoped<AgentOS.Application.Telegram.ITelegramOutbox>(provider => provider.GetRequiredService<TelegramOutbox>());
builder.Services.AddScoped<AgentOS.Application.Telegram.ITelegramOutboxStore>(provider => provider.GetRequiredService<TelegramOutbox>());
builder.Services.AddHostedService<TelegramPollingService>();
builder.Services.Configure<CodexCliOptions>(builder.Configuration.GetSection("Codex"));
builder.Services.AddSingleton<IProcessRunner, ExternalProcessRunner>();
builder.Services.AddSingleton<IAgentAdapter, CodexCliAdapter>();
builder.Services.AddSingleton<OpenAiCompatibleAgentAdapter>();
builder.Services.AddSingleton<AnthropicAgentAdapter>();
builder.Services.AddHttpClient("planner", client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<TaskQueueService>();
builder.Services.AddScoped<RunnerRegistrationService>();
builder.Services.AddScoped<AgentService>();
builder.Services.AddScoped<ModelProfileService>();
builder.Services.AddScoped<SkillService>();
builder.Services.AddScoped<PlannerService>();
builder.Services.AddScoped<PlannerDispatchService>();
builder.Services.AddScoped<BacklogService>();
builder.Services.AddScoped<BacklogPlannerService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<ScheduledJobService>();
builder.Services.Configure<UsageOptions>(builder.Configuration.GetSection("Usage"));
builder.Services.AddScoped<UsageStatisticsService>();
builder.Services.AddScoped<DeadLetterService>();
builder.Services.AddScoped<WorkerAdminService>();
builder.Services.Configure<BrowserAutomationOptions>(builder.Configuration.GetSection("BrowserAutomation"));
builder.Services.AddScoped<BrowserAutomationService>();
builder.Services.Configure<KnowledgeOptions>(builder.Configuration.GetSection("Knowledge"));
builder.Services.AddHttpClient<IEmbeddingClient, OpenAiEmbeddingClient>();
builder.Services.AddScoped<KnowledgeService>();
builder.Services.AddScoped<BusinessEventService>();
builder.Services.AddScoped<PipelineService>();
builder.Services.AddScoped<CodingRunService>();
builder.Services.AddScoped<SubtaskService>();
builder.Services.AddScoped<ApprovalService>();
builder.Services.AddScoped<DecisionMemoryService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<QaService>();
builder.Services.Configure<ReviewOptions>(builder.Configuration.GetSection("Review"));
builder.Services.AddScoped<CodexReviewAgent>();
builder.Services.AddScoped<ApiReviewAgent>();
builder.Services.AddScoped<IReviewAgent, ReviewAgentRouter>();
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

app.UseDefaultFiles();
app.UseStaticFiles();

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
app.MapBacklogEndpoints();
app.MapProjectEndpoints();
app.MapScheduledJobEndpoints();
app.MapUsageEndpoints();
app.MapDeadLetterEndpoints();
app.MapWorkerAdminEndpoints();
app.MapBrowserAutomationEndpoints();
app.MapKnowledgeEndpoints();
app.MapBusinessEventEndpoints();
app.MapPipelineEndpoints();
app.MapCodingRunEndpoints();
app.MapSubtaskEndpoints();
app.MapApprovalEndpoints();
app.MapDecisionMemoryEndpoints();
app.MapGitHubWebhookEndpoints();
app.MapQualityGateEndpoints();
app.MapMergeReadinessEndpoints();
app.MapVersionEndpoints();
app.MapHub<TaskHub>("/hubs/tasks");

app.MapGet("/api", () => Results.Ok(new
{
    service = "AgentOS.Api",
    status = "ok",
    timestampUtc = DateTime.UtcNow
}));

app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed so the integration test host can boot the real application pipeline.</summary>
public partial class Program;
