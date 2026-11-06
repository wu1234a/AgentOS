using AgentOS.Application.Security;
using Microsoft.AspNetCore.SignalR;

namespace AgentOS.Api.Realtime;

/// <summary>
/// UI-only push channel. The database remains the source of truth; clients must refetch REST
/// data after reconnecting. Connections authenticate with the same development API key used by
/// the REST API (AOS-009+ replaces this with user sessions and project scoping).
/// </summary>
public sealed class TaskHub(IApiClientAuthenticator authenticator, ILogger<TaskHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var key = Context.GetHttpContext()?.Request.Query["api_key"].FirstOrDefault();
        var identity = await authenticator.AuthenticateAsync(key, Context.ConnectionAborted);

        if (identity is null)
        {
            logger.LogWarning("Rejected SignalR connection without a valid API key.");
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();
    }
}
