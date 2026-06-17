using System.Globalization;
using Nyautomator;

namespace Nyautomator.VTubeStudio;

public sealed class VtubeStudioAuthService : IAsyncDisposable
{
    private const string IntegrationKey = "vtubestudio";
    private const string MetadataServerUrl = "ServerUrl";
    private const string MetadataPluginName = "PluginName";
    private const string MetadataPluginDeveloper = "PluginDeveloper";
    private const string MetadataLastAuthenticatedUtc = "LastAuthenticatedUtc";
    private const string MetadataLastKnownVtsVersion = "LastKnownVtsVersion";
    private const string MetadataLastError = "LastError";

    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly VtubeStudioClient _client;
    private readonly object _eventStateLock = new();

    private static readonly Dictionary<string, object?> DefaultEventSubscriptions = new(StringComparer.Ordinal)
    {
        ["ModelLoadedEvent"] = new { },
        ["TrackingStatusChangedEvent"] = new { },
        ["HotkeyTriggeredEvent"] = new { ignoreHotkeysTriggeredByAPI = false },
        ["BackgroundChangedEvent"] = new { },
        ["ModelConfigChangedEvent"] = new { }
    };

    private string _serverUrl = "ws://localhost:8001";
    private string _pluginName = "Nyautomator";
    private string _pluginDeveloper = "BlackJaxDev";
    private string? _lastKnownVersion;
    private string? _lastError;
    private DateTime? _lastAuthenticatedUtc;
    private VtubeStudioConnectionState _state = VtubeStudioConnectionState.Disconnected;
    private bool _eventSubscriptionActive;
    private DateTime? _lastEventReceivedUtc;
    private DateTime? _lastSubscriptionAttemptUtc;
    private string? _lastSubscriptionError;
    private int _receivedEventCount;
    private HashSet<string> _subscribedEvents = new(StringComparer.Ordinal);

    public event Action<VtubeStudioLogEntry>? LogEmitted;
    public event Action<VtubeStudioApiEventMessage>? ApiEventReceived;

    public VtubeStudioAuthService()
    {
        _client = new VtubeStudioClient();
        _client.LogEmitted += ForwardClientLog;
        _client.ApiEventReceived += HandleApiEventReceived;
    }

    public void Configure(VtubeStudioConnectionOptions? options)
    {
        _serverUrl = string.IsNullOrWhiteSpace(options?.ServerUrl) ? "ws://localhost:8001" : options.ServerUrl.Trim();
        _pluginName = string.IsNullOrWhiteSpace(options?.PluginName) ? "Nyautomator" : options.PluginName.Trim();
        _pluginDeveloper = string.IsNullOrWhiteSpace(options?.PluginDeveloper) ? "BlackJaxDev" : options.PluginDeveloper.Trim();
    }

    public VtubeStudioStatus GetStatusSnapshot()
    {
        var hasStoredToken = !string.IsNullOrWhiteSpace(IntegrationTokenStore.Get(IntegrationKey)?.AccessToken);
        return BuildStatus(hasStoredToken);
    }

    public async Task<VtubeStudioOperationResult> ConnectAsync(CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _lastError = null;

            await _client.ConnectAsync(_serverUrl, cancellationToken).ConfigureAwait(false);
            var apiState = await _client.GetApiStateAsync(cancellationToken).ConfigureAwait(false);
            _lastKnownVersion = apiState.VTubeStudioVersion;
            _state = apiState.CurrentSessionAuthenticated
                ? VtubeStudioConnectionState.Authenticated
                : VtubeStudioConnectionState.ConnectedUnauthenticated;

            PersistMetadataSnapshot(token: IntegrationTokenStore.Get(IntegrationKey));

            return VtubeStudioOperationResult.CreateSuccess(BuildStatus(), "Connected to VTube Studio.");
        }
        catch (Exception ex)
        {
            _state = VtubeStudioConnectionState.Error;
            _lastError = ex.Message;
            PersistMetadataSnapshot(token: IntegrationTokenStore.Get(IntegrationKey));
            EmitError("ConnectFailed", "Failed to connect to VTube Studio.", ex.Message);
            return VtubeStudioOperationResult.CreateFailure($"Failed to connect to VTube Studio: {ex.Message}", BuildStatus());
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<VtubeStudioOperationResult> DisconnectAsync(bool clearStoredToken, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _client.DisconnectAsync(cancellationToken).ConfigureAwait(false);

            if (clearStoredToken)
            {
                IntegrationTokenStore.Clear(IntegrationKey);
                _lastAuthenticatedUtc = null;
            }

            _state = VtubeStudioConnectionState.Disconnected;
            _lastError = null;
            ClearEventSubscriptionState();

            return VtubeStudioOperationResult.CreateSuccess(BuildStatus(), "Disconnected from VTube Studio.");
        }
        catch (Exception ex)
        {
            _state = VtubeStudioConnectionState.Error;
            _lastError = ex.Message;
            EmitError("DisconnectFailed", "Failed to disconnect from VTube Studio.", ex.Message);
            return VtubeStudioOperationResult.CreateFailure($"Failed to disconnect from VTube Studio: {ex.Message}", BuildStatus());
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<VtubeStudioOperationResult> RequestAuthenticationTokenAsync(string? pluginIconBase64, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_client.IsConnected)
                return VtubeStudioOperationResult.CreateFailure("Connect to VTube Studio before requesting an authentication token.", BuildStatus());

            var token = await _client.RequestAuthenticationTokenAsync(_pluginName, _pluginDeveloper, pluginIconBase64, cancellationToken)
                .ConfigureAwait(false);

            var integrationToken = IntegrationTokenStore.Get(IntegrationKey) ?? new IntegrationToken();
            integrationToken.AccessToken = token;
            integrationToken.Metadata = EnsureMetadata(integrationToken.Metadata);
            integrationToken.Metadata[MetadataPluginName] = _pluginName;
            integrationToken.Metadata[MetadataPluginDeveloper] = _pluginDeveloper;
            integrationToken.Metadata[MetadataServerUrl] = _serverUrl;

            IntegrationTokenStore.Set(IntegrationKey, integrationToken);
            _state = VtubeStudioConnectionState.ConnectedUnauthenticated;
            _lastError = null;
            PersistMetadataSnapshot(integrationToken);

            EmitInfo("AuthTokenStored", "Stored VTube Studio authentication token.");
            return VtubeStudioOperationResult.CreateSuccess(BuildStatus(), "Authentication token received and stored.");
        }
        catch (VtubeStudioApiException ex)
        {
            _state = VtubeStudioConnectionState.Error;
            _lastError = ex.Message;
            PersistMetadataSnapshot(IntegrationTokenStore.Get(IntegrationKey));
            EmitWarning("AuthTokenRequestFailed", "VTube Studio rejected authentication token request.", $"errorId={ex.ErrorId}; message={ex.Message}");
            return VtubeStudioOperationResult.CreateFailure($"VTube Studio token request failed: {ex.Message}", BuildStatus());
        }
        catch (Exception ex)
        {
            _state = VtubeStudioConnectionState.Error;
            _lastError = ex.Message;
            PersistMetadataSnapshot(IntegrationTokenStore.Get(IntegrationKey));
            EmitError("AuthTokenRequestError", "Failed to request VTube Studio authentication token.", ex.Message);
            return VtubeStudioOperationResult.CreateFailure($"Failed to request authentication token: {ex.Message}", BuildStatus());
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<VtubeStudioOperationResult> AuthenticateWithStoredTokenAsync(CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_client.IsConnected)
                return VtubeStudioOperationResult.CreateFailure("Connect to VTube Studio before authenticating a session.", BuildStatus());

            var token = IntegrationTokenStore.Get(IntegrationKey);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                _state = VtubeStudioConnectionState.ConnectedUnauthenticated;
                _lastError = "No stored VTube Studio token available.";
                return VtubeStudioOperationResult.CreateFailure("No stored VTube Studio token available.", BuildStatus());
            }

            token.Metadata = EnsureMetadata(token.Metadata);

            if (token.Metadata.TryGetValue(MetadataPluginName, out var storedPluginName) && !string.IsNullOrWhiteSpace(storedPluginName))
                _pluginName = storedPluginName;

            if (token.Metadata.TryGetValue(MetadataPluginDeveloper, out var storedPluginDeveloper) && !string.IsNullOrWhiteSpace(storedPluginDeveloper))
                _pluginDeveloper = storedPluginDeveloper;

            var authResponse = await _client.AuthenticateSessionAsync(
                    _pluginName,
                    _pluginDeveloper,
                    token.AccessToken,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!authResponse.Authenticated)
            {
                var reason = string.IsNullOrWhiteSpace(authResponse.Reason)
                    ? "Token rejected by VTube Studio."
                    : authResponse.Reason;

                IntegrationTokenStore.Clear(IntegrationKey);
                _state = VtubeStudioConnectionState.ConnectedUnauthenticated;
                _lastError = reason;
                EmitWarning("AuthRejected", "Stored VTube Studio token was rejected and cleared.", reason);
                return VtubeStudioOperationResult.CreateFailure(reason, BuildStatus(hasStoredTokenOverride: false));
            }

            var apiState = await _client.GetApiStateAsync(cancellationToken).ConfigureAwait(false);
            _lastKnownVersion = apiState.VTubeStudioVersion;
            _lastAuthenticatedUtc = DateTime.UtcNow;
            _lastError = null;
            _state = VtubeStudioConnectionState.Authenticated;

            await EnsureDefaultEventSubscriptionsAsync(cancellationToken).ConfigureAwait(false);

            token.Metadata[MetadataServerUrl] = _serverUrl;
            token.Metadata[MetadataPluginName] = _pluginName;
            token.Metadata[MetadataPluginDeveloper] = _pluginDeveloper;
            token.Metadata[MetadataLastAuthenticatedUtc] = _lastAuthenticatedUtc.Value.ToString("O", CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(_lastKnownVersion))
                token.Metadata[MetadataLastKnownVtsVersion] = _lastKnownVersion;
            token.Metadata.Remove(MetadataLastError);

            IntegrationTokenStore.Set(IntegrationKey, token);

            EmitInfo("Authenticated", "Authenticated with VTube Studio successfully.");
            return VtubeStudioOperationResult.CreateSuccess(BuildStatus(), authResponse.Reason ?? "Authenticated with VTube Studio.");
        }
        catch (VtubeStudioApiException ex)
        {
            var shouldClearToken = ex.ErrorId == 50;
            if (shouldClearToken)
                IntegrationTokenStore.Clear(IntegrationKey);

            _state = _client.IsConnected
                ? VtubeStudioConnectionState.ConnectedUnauthenticated
                : VtubeStudioConnectionState.Error;

            _lastError = ex.Message;
            PersistMetadataSnapshot(IntegrationTokenStore.Get(IntegrationKey));

            var message = shouldClearToken
                ? "VTube Studio access was denied or revoked. Stored token has been cleared."
                : $"VTube Studio authentication failed: {ex.Message}";

            EmitWarning("AuthApiError", "VTube Studio authentication API error.", $"errorId={ex.ErrorId}; message={ex.Message}");
            return VtubeStudioOperationResult.CreateFailure(message, BuildStatus());
        }
        catch (Exception ex)
        {
            _state = VtubeStudioConnectionState.Error;
            _lastError = ex.Message;
            PersistMetadataSnapshot(IntegrationTokenStore.Get(IntegrationKey));
            EmitError("AuthError", "Unexpected VTube Studio authentication error.", ex.Message);
            return VtubeStudioOperationResult.CreateFailure($"Failed to authenticate with VTube Studio: {ex.Message}", BuildStatus());
        }
        finally
        {
            _sync.Release();
        }
    }

    public void ClearStoredToken()
    {
        IntegrationTokenStore.Clear(IntegrationKey);
        _state = _client.IsConnected
            ? VtubeStudioConnectionState.ConnectedUnauthenticated
            : VtubeStudioConnectionState.Disconnected;
        _lastAuthenticatedUtc = null;
        _lastError = null;
        ClearEventSubscriptionState();
        EmitInfo("TokenCleared", "Cleared stored VTube Studio token.");
    }

    public async Task<VtubeStudioOperationResult> SubscribeEventAsync(string eventName, object? config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return VtubeStudioOperationResult.CreateFailure("Event name is required.", BuildStatus());

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_client.IsConnected)
                return VtubeStudioOperationResult.CreateFailure("Connect to VTube Studio before subscribing to events.", BuildStatus());

            if (_state != VtubeStudioConnectionState.Authenticated)
                return VtubeStudioOperationResult.CreateFailure("Authenticate VTube Studio session before subscribing to events.", BuildStatus());

            await ApplyEventSubscriptionAsync(eventName.Trim(), subscribe: true, config, cancellationToken).ConfigureAwait(false);
            return VtubeStudioOperationResult.CreateSuccess(BuildStatus(), $"Subscribed to '{eventName.Trim()}'.");
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return VtubeStudioOperationResult.CreateFailure(ex.Message, BuildStatus());
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<VtubeStudioOperationResult> UnsubscribeAllEventsAsync(CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_client.IsConnected)
                return VtubeStudioOperationResult.CreateFailure("Connect to VTube Studio before managing event subscriptions.", BuildStatus());

            if (_state != VtubeStudioConnectionState.Authenticated)
                return VtubeStudioOperationResult.CreateFailure("Authenticate VTube Studio session before managing event subscriptions.", BuildStatus());

            await ApplyEventSubscriptionAsync(eventName: null, subscribe: false, config: null, cancellationToken).ConfigureAwait(false);
            return VtubeStudioOperationResult.CreateSuccess(BuildStatus(), "Unsubscribed from all VTube Studio events.");
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return VtubeStudioOperationResult.CreateFailure(ex.Message, BuildStatus());
        }
        finally
        {
            _sync.Release();
        }
    }

    public Task<VtubeStudioDataResult<VtubeStudioCurrentModelData>> GetCurrentModelAsync(CancellationToken cancellationToken)
        => ExecuteCommandAsync(
            operationName: "CurrentModel",
            action: async ct => await _client.GetCurrentModelAsync(ct).ConfigureAwait(false),
            cancellationToken);

    public Task<VtubeStudioDataResult<VtubeStudioAvailableModelsData>> GetAvailableModelsAsync(CancellationToken cancellationToken)
        => ExecuteCommandAsync(
            operationName: "AvailableModels",
            action: async ct => await _client.GetAvailableModelsAsync(ct).ConfigureAwait(false),
            cancellationToken);

    public Task<VtubeStudioDataResult<VtubeStudioModelLoadResponseData>> LoadModelAsync(string? modelId, CancellationToken cancellationToken)
        => ExecuteCommandAsync(
            operationName: "ModelLoad",
            action: async ct => await _client.LoadModelAsync(modelId, ct).ConfigureAwait(false),
            cancellationToken);

    public Task<VtubeStudioDataResult<VtubeStudioHotkeysData>> GetHotkeysAsync(string? modelId, string? live2DItemFileName, CancellationToken cancellationToken)
        => ExecuteCommandAsync(
            operationName: "Hotkeys",
            action: async ct => await _client.GetHotkeysAsync(modelId, live2DItemFileName, ct).ConfigureAwait(false),
            cancellationToken);

    public Task<VtubeStudioDataResult<VtubeStudioHotkeyTriggerResponseData>> TriggerHotkeyAsync(string hotkeyId, string? itemInstanceId, CancellationToken cancellationToken)
        => ExecuteCommandAsync(
            operationName: "HotkeyTrigger",
            action: async ct => await _client.TriggerHotkeyAsync(hotkeyId, itemInstanceId, ct).ConfigureAwait(false),
            cancellationToken);

    public Task<VtubeStudioDataResult<VtubeStudioExpressionStateData>> GetExpressionStateAsync(bool details, string? expressionFile, CancellationToken cancellationToken)
        => ExecuteCommandAsync(
            operationName: "ExpressionState",
            action: async ct => await _client.GetExpressionStateAsync(details, expressionFile, ct).ConfigureAwait(false),
            cancellationToken);

    public Task<VtubeStudioDataResult<object>> SetExpressionStateAsync(string expressionFile, bool active, double fadeTime, CancellationToken cancellationToken)
        => ExecuteCommandAsync(
            operationName: "ExpressionActivation",
            action: async ct =>
            {
                await _client.SetExpressionStateAsync(expressionFile, active, fadeTime, ct).ConfigureAwait(false);
                return (object?)null;
            },
            cancellationToken);

    public Task<VtubeStudioDataResult<VtubeStudioParameterValueData>> GetParameterValueAsync(string parameterName, CancellationToken cancellationToken)
        => ExecuteCommandAsync(
            operationName: "ParameterValue",
            action: async ct => await _client.GetParameterValueAsync(parameterName, ct).ConfigureAwait(false),
            cancellationToken);

    public Task<VtubeStudioDataResult<object>> InjectParameterDataAsync(VtubeStudioInjectParameterDataRequest request, CancellationToken cancellationToken)
        => ExecuteCommandAsync(
            operationName: "InjectParameterData",
            action: async ct =>
            {
                await _client.InjectParameterDataAsync(request, ct).ConfigureAwait(false);
                return (object?)null;
            },
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _client.LogEmitted -= ForwardClientLog;
        _client.ApiEventReceived -= HandleApiEventReceived;
        await _client.DisposeAsync().ConfigureAwait(false);
        _sync.Dispose();
    }

    private VtubeStudioStatus BuildStatus(bool? hasStoredTokenOverride = null)
    {
        var hasStoredToken = hasStoredTokenOverride ?? !string.IsNullOrWhiteSpace(IntegrationTokenStore.Get(IntegrationKey)?.AccessToken);

        bool eventSubscriptionActive;
        DateTime? lastEventReceivedUtc;
        DateTime? lastSubscriptionAttemptUtc;
        string? lastSubscriptionError;
        int receivedEventCount;
        List<string> subscribedEvents;

        lock (_eventStateLock)
        {
            eventSubscriptionActive = _eventSubscriptionActive;
            lastEventReceivedUtc = _lastEventReceivedUtc;
            lastSubscriptionAttemptUtc = _lastSubscriptionAttemptUtc;
            lastSubscriptionError = _lastSubscriptionError;
            receivedEventCount = _receivedEventCount;
            subscribedEvents = [.. _subscribedEvents.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)];
        }

        return new VtubeStudioStatus
        {
            State = _state,
            IsConnected = _client.IsConnected,
            IsAuthenticated = _state == VtubeStudioConnectionState.Authenticated,
            HasStoredToken = hasStoredToken,
            Endpoint = _serverUrl,
            PluginName = _pluginName,
            PluginDeveloper = _pluginDeveloper,
            VTubeStudioVersion = _lastKnownVersion,
            LastError = _lastError,
            LastAuthenticatedUtc = _lastAuthenticatedUtc,
            EventSubscriptionActive = eventSubscriptionActive,
            LastEventReceivedUtc = lastEventReceivedUtc,
            LastSubscriptionAttemptUtc = lastSubscriptionAttemptUtc,
            LastSubscriptionError = lastSubscriptionError,
            ReceivedEventCount = receivedEventCount,
            SubscribedEvents = subscribedEvents,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private async Task EnsureDefaultEventSubscriptionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var pair in DefaultEventSubscriptions)
                await ApplyEventSubscriptionAsync(pair.Key, subscribe: true, pair.Value, cancellationToken).ConfigureAwait(false);

            EmitInfo("EventSubscriptionsReady", "Applied default VTube Studio event subscriptions.");
        }
        catch (Exception ex)
        {
            lock (_eventStateLock)
            {
                _lastSubscriptionError = ex.Message;
            }
            EmitWarning("EventSubscriptionsFailed", "Failed to apply one or more VTube Studio event subscriptions.", ex.Message);
        }
    }

    private async Task ApplyEventSubscriptionAsync(string? eventName, bool subscribe, object? config, CancellationToken cancellationToken)
    {
        lock (_eventStateLock)
        {
            _lastSubscriptionAttemptUtc = DateTime.UtcNow;
        }

        var response = await _client
            .SetEventSubscriptionAsync(eventName, subscribe, config, cancellationToken)
            .ConfigureAwait(false);

        var normalizedEvents = response.SubscribedEvents
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim());

        lock (_eventStateLock)
        {
            _subscribedEvents = new HashSet<string>(normalizedEvents, StringComparer.Ordinal);
            _eventSubscriptionActive = _subscribedEvents.Count > 0;
            _lastSubscriptionError = null;
        }
    }

    private void HandleApiEventReceived(VtubeStudioApiEventMessage apiEvent)
    {
        lock (_eventStateLock)
        {
            _lastEventReceivedUtc = apiEvent.ReceivedAtUtc;
            _receivedEventCount++;
        }

        try
        {
            ApiEventReceived?.Invoke(apiEvent);
        }
        catch
        {
            // observer errors must not affect execution flow
        }
    }

    private void ClearEventSubscriptionState()
    {
        lock (_eventStateLock)
        {
            _eventSubscriptionActive = false;
            _subscribedEvents.Clear();
            _lastSubscriptionError = null;
        }
    }

    private Dictionary<string, string> EnsureMetadata(Dictionary<string, string>? metadata)
        => metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);

    private void PersistMetadataSnapshot(IntegrationToken? token)
    {
        if (token is null)
            return;

        token.Metadata = EnsureMetadata(token.Metadata);
        token.Metadata[MetadataServerUrl] = _serverUrl;
        token.Metadata[MetadataPluginName] = _pluginName;
        token.Metadata[MetadataPluginDeveloper] = _pluginDeveloper;

        if (!string.IsNullOrWhiteSpace(_lastKnownVersion))
            token.Metadata[MetadataLastKnownVtsVersion] = _lastKnownVersion;

        if (_lastAuthenticatedUtc.HasValue)
            token.Metadata[MetadataLastAuthenticatedUtc] = _lastAuthenticatedUtc.Value.ToString("O", CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(_lastError))
            token.Metadata[MetadataLastError] = _lastError;
        else
            token.Metadata.Remove(MetadataLastError);

        IntegrationTokenStore.Set(IntegrationKey, token);
    }

    private void ForwardClientLog(VtubeStudioLogEntry entry)
    {
        try
        {
            LogEmitted?.Invoke(entry);
        }
        catch
        {
            // observer errors must not affect execution flow
        }
    }

    private void EmitInfo(string eventName, string message, string? detail = null)
        => EmitLog("Information", eventName, message, detail);

    private void EmitWarning(string eventName, string message, string? detail = null)
        => EmitLog("Warning", eventName, message, detail);

    private void EmitError(string eventName, string message, string? detail = null)
        => EmitLog("Error", eventName, message, detail);

    private void EmitLog(string level, string eventName, string message, string? detail)
    {
        var entry = new VtubeStudioLogEntry(DateTime.UtcNow, level, eventName, message, detail);
        try
        {
            LogEmitted?.Invoke(entry);
        }
        catch
        {
            // observer errors must not affect execution flow
        }
    }

    private async Task<VtubeStudioDataResult<T>> ExecuteCommandAsync<T>(
        string operationName,
        Func<CancellationToken, Task<T?>> action,
        CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_client.IsConnected)
                return VtubeStudioDataResult<T>.CreateFailure("Connect to VTube Studio before executing commands.", BuildStatus());

            if (_state != VtubeStudioConnectionState.Authenticated)
                return VtubeStudioDataResult<T>.CreateFailure("Authenticate VTube Studio session before executing commands.", BuildStatus());

            var data = await action(cancellationToken).ConfigureAwait(false);
            _lastError = null;

            try
            {
                var apiState = await _client.GetApiStateAsync(cancellationToken).ConfigureAwait(false);
                _lastKnownVersion = apiState.VTubeStudioVersion;
            }
            catch
            {
                // Best-effort metadata refresh.
            }

            return VtubeStudioDataResult<T>.CreateSuccess(data, BuildStatus());
        }
        catch (VtubeStudioApiException ex)
        {
            _lastError = ex.Message;

            var shouldClearToken = ex.ErrorId == 50;
            if (shouldClearToken)
            {
                IntegrationTokenStore.Clear(IntegrationKey);
                _state = _client.IsConnected
                    ? VtubeStudioConnectionState.ConnectedUnauthenticated
                    : VtubeStudioConnectionState.Error;
            }

            EmitWarning(
                $"{operationName}ApiError",
                $"VTube Studio command '{operationName}' returned API error.",
                $"errorId={ex.ErrorId}; message={ex.Message}");

            return VtubeStudioDataResult<T>.CreateFailure(
                shouldClearToken
                    ? "VTube Studio access was denied or revoked. Stored token has been cleared."
                    : ex.Message,
                BuildStatus());
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            EmitError($"{operationName}Error", $"VTube Studio command '{operationName}' failed.", ex.Message);
            return VtubeStudioDataResult<T>.CreateFailure(ex.Message, BuildStatus());
        }
        finally
        {
            _sync.Release();
        }
    }
}
