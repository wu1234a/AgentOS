using AgentOS.Application.Tasks;
using AgentOS.Domain.Events;

namespace AgentOS.Api.Tests.Fakes;

/// <summary>Configurable dispatcher stand-in for event-service tests.</summary>
public sealed class FakeTaskDispatcher : ITaskDispatcher
{
    public DispatchResult Result { get; set; } = new(0, 0, EventStatus.Ignored);

    public int Calls { get; private set; }

    public Task<DispatchResult> DispatchAsync(Event evt, CancellationToken cancellationToken)
    {
        Calls++;

        // Mirror the real dispatcher: the event carries the status it was left in.
        evt.Status = Result.Status;
        return Task.FromResult(Result);
    }
}
