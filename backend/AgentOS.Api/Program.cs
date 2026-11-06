using AgentOS.Api.Endpoints;
using AgentOS.Api.Filters;
using AgentOS.Api.Health;
using AgentOS.Api.Security;
using AgentOS.Application.Events;
using AgentOS.Application.Rules;
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
builder.Services.AddAgentOSInfrastructure(builder.Configuration);

builder.Services.AddSingleton<CreateEventValidator>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<RuleValidator>();
builder.Services.AddScoped<RuleService>();
builder.Services.AddScoped<ITaskDispatcher, TaskDispatchService>();
builder.Services.Configure<TaskQueueOptions>(builder.Configuration.GetSection("TaskQueue"));
builder.Services.Configure<RunnerRegistrationOptions>(builder.Configuration.GetSection("Runner"));
builder.Services.Configure<SkillContentOptions>(builder.Configuration.GetSection("Skills"));
builder.Services.AddScoped<TaskQueueService>();
builder.Services.AddScoped<RunnerRegistrationService>();
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

app.MapGet("/", () => Results.Ok(new
{
    service = "AgentOS.Api",
    status = "ok",
    timestampUtc = DateTime.UtcNow
}));

app.Run();

/// <summary>Exposed so the integration test host can boot the real application pipeline.</summary>
public partial class Program;
