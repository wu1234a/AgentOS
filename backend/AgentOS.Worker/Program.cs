using AgentOS.Application.Tasks;
using AgentOS.Infrastructure;
using AgentOS.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAgentOSInfrastructure(builder.Configuration);
builder.Services.Configure<TaskQueueOptions>(builder.Configuration.GetSection("TaskQueue"));
builder.Services.AddScoped<LeaseRecoveryService>();
builder.Services.AddHostedService<LeaseRecoveryWorker>();

var host = builder.Build();
host.Run();
