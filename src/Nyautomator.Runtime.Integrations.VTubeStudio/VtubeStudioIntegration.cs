using Nyautomator.Runtime.Abstractions;
using Nyautomator.Events;
using Nyautomator.VTubeStudio;

namespace Nyautomator.Runtime.Integrations.VTubeStudio;

public sealed class VtubeStudioIntegration : IVtubeStudioIntegration, ILocalOnlyRuntimeParticipant
{
    private const int StartupRetryAttempts = 4;
    private static readonly TimeSpan[] StartupRetryDelays =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2)
    ];

    private readonly VtubeStudioAuthService _auth = new();
    private readonly VtubeStudioEventSource _eventSource;
    private bool _disposed;
    private bool _isConfigured;
    private bool _enabled;
    private bool _autoReconnect = true;

    public VtubeStudioIntegration()
    {
        _auth.LogEmitted += ForwardLog;
        _eventSource = new VtubeStudioEventSource(_auth);
    }

    public string Name => "VTubeStudio";

    public VtubeStudioAuthService Auth => _auth;
    public IEventSource EventSource => _eventSource;

    public event Action<VtubeStudioLogEntry>? LogEmitted;

    public Task ConfigureAsync(AppConfiguration config, CancellationToken cancellationToken)
        => ConfigureAsync(new VtubeStudioConnectionOptions(), cancellationToken);

    public Task ConfigureAsync(VtubeStudioConnectionOptions options, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        options ??= new VtubeStudioConnectionOptions();
        _auth.Configure(options);
        _enabled = options.Enabled;
        _autoReconnect = options.AutoReconnect;
        _isConfigured = true;

        return Task.CompletedTask;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (!_isConfigured || !_enabled || !_autoReconnect)
            return;

        try
        {
            var connectResult = await ConnectWithRetryAsync(cancellationToken).ConfigureAwait(false);
            if (!connectResult.Success)
                return;

            if (connectResult.Status.HasStoredToken)
                await AuthenticateWithRetryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown path.
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        try
        {
            await _auth.DisconnectAsync(clearStoredToken: false, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown path.
        }
    }

    public VtubeStudioStatus GetStatusSnapshot()
    {
        ThrowIfDisposed();

        var status = _auth.GetStatusSnapshot();
        var forwarding = _eventSource.GetSnapshot();

        return new VtubeStudioStatus
        {
            State = status.State,
            IsConnected = status.IsConnected,
            IsAuthenticated = status.IsAuthenticated,
            HasStoredToken = status.HasStoredToken,
            Endpoint = status.Endpoint,
            PluginName = status.PluginName,
            PluginDeveloper = status.PluginDeveloper,
            VTubeStudioVersion = status.VTubeStudioVersion,
            LastError = status.LastError,
            LastAuthenticatedUtc = status.LastAuthenticatedUtc,
            EventSubscriptionActive = status.EventSubscriptionActive,
            LastEventReceivedUtc = status.LastEventReceivedUtc,
            LastSubscriptionAttemptUtc = status.LastSubscriptionAttemptUtc,
            LastSubscriptionError = status.LastSubscriptionError,
            ReceivedEventCount = status.ReceivedEventCount,
            SubscribedEvents = status.SubscribedEvents,
            LastForwardedEventUtc = forwarding.LastForwardedEventUtc,
            ForwardedEventCount = forwarding.ForwardedEventCount,
            ThrottledEventCount = forwarding.ThrottledEventCount,
            UpdatedAtUtc = status.UpdatedAtUtc
        };
    }

    public VtubeStudioEventForwardingSnapshot GetEventForwardingSnapshot()
    {
        ThrowIfDisposed();
        return _eventSource.GetSnapshot();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _auth.LogEmitted -= ForwardLog;
        _eventSource.Dispose();
        await _auth.DisposeAsync().ConfigureAwait(false);
    }

    private void ForwardLog(VtubeStudioLogEntry entry)
    {
        try
        {
            LogEmitted?.Invoke(entry);
        }
        catch
        {
            // Ignore observer failures.
        }
    }

    private async Task<VtubeStudioOperationResult> ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        VtubeStudioOperationResult lastResult = VtubeStudioOperationResult.CreateFailure("VTube Studio connect failed.", _auth.GetStatusSnapshot());

        for (var attempt = 1; attempt <= StartupRetryAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lastResult = await _auth.ConnectAsync(cancellationToken).ConfigureAwait(false);
            if (lastResult.Success)
                return lastResult;

            if (attempt >= StartupRetryAttempts || !IsTransientFailure(lastResult.Error, lastResult.Message))
                return lastResult;

            var delay = StartupRetryDelays[Math.Min(attempt - 1, StartupRetryDelays.Length - 1)];
            EmitInfo("ConnectRetry", $"VTube Studio connect attempt {attempt} failed; retrying in {delay.TotalMilliseconds:0}ms.", lastResult.Error ?? lastResult.Message);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        return lastResult;
    }

    private async Task<VtubeStudioOperationResult> AuthenticateWithRetryAsync(CancellationToken cancellationToken)
    {
        VtubeStudioOperationResult lastResult = VtubeStudioOperationResult.CreateFailure("VTube Studio authentication failed.", _auth.GetStatusSnapshot());

        for (var attempt = 1; attempt <= StartupRetryAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lastResult = await _auth.AuthenticateWithStoredTokenAsync(cancellationToken).ConfigureAwait(false);
            if (lastResult.Success)
                return lastResult;

            if (attempt >= StartupRetryAttempts || !IsTransientFailure(lastResult.Error, lastResult.Message))
                return lastResult;

            var delay = StartupRetryDelays[Math.Min(attempt - 1, StartupRetryDelays.Length - 1)];
            EmitInfo("AuthRetry", $"VTube Studio auth attempt {attempt} failed; retrying in {delay.TotalMilliseconds:0}ms.", lastResult.Error ?? lastResult.Message);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        return lastResult;
    }

    private static bool IsTransientFailure(string? error, string? message)
    {
        var text = $"{error} {message}".Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || text.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || text.Contains("socket", StringComparison.OrdinalIgnoreCase)
            || text.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || text.Contains("closed", StringComparison.OrdinalIgnoreCase)
            || text.Contains("reset", StringComparison.OrdinalIgnoreCase)
            || text.Contains("refused", StringComparison.OrdinalIgnoreCase)
            || text.Contains("unreachable", StringComparison.OrdinalIgnoreCase)
            || text.Contains("temporar", StringComparison.OrdinalIgnoreCase);
    }

    private void EmitInfo(string eventName, string message, string? detail = null)
    {
        var entry = new VtubeStudioLogEntry(DateTime.UtcNow, "Information", eventName, message, detail);
        ForwardLog(entry);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VtubeStudioIntegration));
    }
}
