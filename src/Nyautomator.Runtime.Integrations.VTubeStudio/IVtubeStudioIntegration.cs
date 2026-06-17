using Nyautomator.Runtime.Abstractions;
using Nyautomator.Events;
using Nyautomator.VTubeStudio;

namespace Nyautomator.Runtime.Integrations.VTubeStudio;

public interface IVtubeStudioIntegration : IRuntimeParticipant
{
    VtubeStudioAuthService Auth { get; }
    IEventSource EventSource { get; }
    Task ConfigureAsync(VtubeStudioConnectionOptions options, CancellationToken cancellationToken);
    VtubeStudioStatus GetStatusSnapshot();
    VtubeStudioEventForwardingSnapshot GetEventForwardingSnapshot();
    event Action<VtubeStudioLogEntry>? LogEmitted;
}
