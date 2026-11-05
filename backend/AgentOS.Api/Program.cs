using AgentOS.Api.Health;
using AgentOS.Infrastructure.Health;
using AgentOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

builder.Services.AddProblemDetails();

var connectionString = builder.Configuration.GetConnectionString("AgentOS")
    ?? throw new InvalidOperationException("ConnectionStrings:AgentOS is not configured.");

builder.Services.AddDbContext<AgentOSDbContext>(options => options.UseSqlServer(connectionString));

builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sqlserver", tags: ["ready"]);

builder.Services.AddCors(options => options.AddPolicy("web-dev", policy => policy
    .WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

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

app.MapGet("/", () => Results.Ok(new
{
    service = "AgentOS.Api",
    status = "ok",
    timestampUtc = DateTime.UtcNow
}));

app.Run();

/// <summary>Exposed so the integration test host can boot the real application pipeline.</summary>
public partial class Program;
