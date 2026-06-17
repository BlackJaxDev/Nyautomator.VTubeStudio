using System.Collections.Concurrent;
using Nyautomator.Commands;
using Nyautomator.Events;
using Nyautomator.VTubeStudio;

namespace Nyautomator.Runtime.Integrations.VTubeStudio;

public sealed record VtubeStudioEventForwardingSnapshot(
    DateTime? LastForwardedEventUtc,
    int ForwardedEventCount,
    int ThrottledEventCount);

public sealed class VtubeStudioEventSource : IEventSource, IDisposable
{
    private readonly ConcurrentDictionary<string, DateTime> _lastForwardedByEventName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IEvent> _eventByName = new(StringComparer.Ordinal)
    {
        ["ModelLoadedEvent"] = new VtubeStudioEvent("VTube Studio Model Loaded", typeof(VtubeStudioEventSource)),
        ["TrackingStatusChangedEvent"] = new VtubeStudioEvent("VTube Studio Tracking Status Changed", typeof(VtubeStudioEventSource)),
        ["HotkeyTriggeredEvent"] = new VtubeStudioEvent("VTube Studio Hotkey Triggered", typeof(VtubeStudioEventSource)),
        ["BackgroundChangedEvent"] = new VtubeStudioEvent("VTube Studio Background Changed", typeof(VtubeStudioEventSource)),
        ["ModelConfigChangedEvent"] = new VtubeStudioEvent("VTube Studio Model Config Changed", typeof(VtubeStudioEventSource)),
        ["ModelMovedEvent"] = new VtubeStudioEvent("VTube Studio Model Moved", typeof(VtubeStudioEventSource)),
        ["ModelOutlineEvent"] = new VtubeStudioEvent("VTube Studio Model Outline Changed", typeof(VtubeStudioEventSource))
    };

    private readonly Dictionary<string, TimeSpan> _throttleWindows = new(StringComparer.Ordinal)
    {
        ["ModelMovedEvent"] = TimeSpan.FromMilliseconds(250),
        ["ModelOutlineEvent"] = TimeSpan.FromMilliseconds(500),
        ["TestEvent"] = TimeSpan.FromMilliseconds(1000)
    };

    private readonly VtubeStudioAuthService _auth;
    private int _forwardedEventCount;
    private int _throttledEventCount;
    private DateTime? _lastForwardedEventUtc;
    private bool _disposed;

    public VtubeStudioEventSource(VtubeStudioAuthService auth)
    {
        _auth = auth;
        _auth.ApiEventReceived += OnApiEventReceived;
    }

    public event EventHandler<EventReceivedEventArgs>? EventReceived;

    public VtubeStudioEventForwardingSnapshot GetSnapshot()
        => new(_lastForwardedEventUtc, _forwardedEventCount, _throttledEventCount);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _auth.ApiEventReceived -= OnApiEventReceived;
    }

    private void OnApiEventReceived(VtubeStudioApiEventMessage apiEvent)
    {
        if (!_eventByName.TryGetValue(apiEvent.EventName, out var mappedEvent))
            return;

        if (ShouldThrottle(apiEvent.EventName, apiEvent.ReceivedAtUtc))
        {
            Interlocked.Increment(ref _throttledEventCount);
            return;
        }

        _lastForwardedEventUtc = apiEvent.ReceivedAtUtc;
        Interlocked.Increment(ref _forwardedEventCount);

        try
        {
            EventReceived?.Invoke(this, new EventReceivedEventArgs(mappedEvent));
        }
        catch
        {
            // observer errors must not affect event ingestion
        }
    }

    private bool ShouldThrottle(string eventName, DateTime nowUtc)
    {
        if (!_throttleWindows.TryGetValue(eventName, out var throttleWindow))
            return false;

        var previous = _lastForwardedByEventName.GetOrAdd(eventName, DateTime.MinValue);
        if (previous != DateTime.MinValue && nowUtc - previous < throttleWindow)
            return true;

        _lastForwardedByEventName[eventName] = nowUtc;
        return false;
    }

    private sealed class VtubeStudioEvent(string name, Type eventType) : IEvent
    {
        public string Name { get; } = name;
        public Type EventType { get; } = eventType;
    }
}
