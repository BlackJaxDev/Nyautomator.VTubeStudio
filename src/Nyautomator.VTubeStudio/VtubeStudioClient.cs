using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Nyautomator.VTubeStudio;

public sealed class VtubeStudioClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<VtubeStudioResponseEnvelope>> _pending = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveLoopCts;
    private Task? _receiveLoopTask;
    private int _requestSequence;
    private readonly SemaphoreSlim _modelLoadLock = new(1, 1);
    private DateTime _lastModelLoadUtc = DateTime.MinValue;

    public event Action<VtubeStudioLogEntry>? LogEmitted;
    public event Action<VtubeStudioApiEventMessage>? ApiEventReceived;

    public Uri? Endpoint { get; private set; }

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public async Task ConnectAsync(string serverUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var endpoint))
            throw new InvalidOperationException($"Invalid VTube Studio endpoint: '{serverUrl}'.");

        await ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
    }

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
                return;

            await DisconnectInternalAsync().ConfigureAwait(false);

            _socket = new ClientWebSocket();
            _receiveLoopCts = new CancellationTokenSource();
            Endpoint = endpoint;

            EmitInfo("ConnectAttempt", $"Connecting to VTube Studio at {endpoint}.");
            await _socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            EmitInfo("Connected", $"Connected to VTube Studio at {endpoint}.");

            _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_socket, _receiveLoopCts.Token));
        }
        catch (Exception ex)
        {
            EmitError("ConnectFailed", "Failed to connect to VTube Studio.", ex.Message);
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisconnectInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<VtubeStudioApiStateData> GetApiStateAsync(CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync("APIStateRequest", data: null, cancellationToken).ConfigureAwait(false);

        if (string.Equals(response.MessageType, "APIError", StringComparison.Ordinal))
        {
            var apiError = response.DeserializeData<VtubeStudioApiErrorData>(_jsonOptions);
            var errorId = apiError?.ErrorId ?? -1;
            var message = apiError?.Message ?? "Unknown VTube Studio API error.";
            throw new VtubeStudioApiException(errorId, message);
        }

        if (!string.Equals(response.MessageType, "APIStateResponse", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected response type '{response.MessageType}' for APIStateRequest.");

        var data = response.DeserializeData<VtubeStudioApiStateData>(_jsonOptions) ?? new VtubeStudioApiStateData();
        EmitInfo(
            "ApiState",
            "Fetched VTube Studio API state.",
            $"active={data.Active}, authenticated={data.CurrentSessionAuthenticated}, version={data.VTubeStudioVersion ?? "unknown"}");
        return data;
    }

    public async Task<string> RequestAuthenticationTokenAsync(
        string pluginName,
        string pluginDeveloper,
        string? pluginIcon,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pluginName))
            throw new ArgumentException("Plugin name is required.", nameof(pluginName));
        if (string.IsNullOrWhiteSpace(pluginDeveloper))
            throw new ArgumentException("Plugin developer is required.", nameof(pluginDeveloper));

        var payload = new VtubeStudioAuthenticationTokenRequestData
        {
            PluginName = pluginName.Trim(),
            PluginDeveloper = pluginDeveloper.Trim(),
            PluginIcon = string.IsNullOrWhiteSpace(pluginIcon) ? null : pluginIcon.Trim()
        };

        var response = await SendRequestAsync("AuthenticationTokenRequest", payload, cancellationToken).ConfigureAwait(false);
        ThrowIfApiError(response);

        if (!string.Equals(response.MessageType, "AuthenticationTokenResponse", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected response type '{response.MessageType}' for AuthenticationTokenRequest.");

        var data = response.DeserializeData<VtubeStudioAuthenticationTokenResponseData>(_jsonOptions);
        var token = data?.AuthenticationToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("VTube Studio returned an empty authentication token.");

        EmitInfo("AuthTokenReceived", "Received VTube Studio authentication token.");
        return token;
    }

    public async Task<VtubeStudioAuthenticationResponseData> AuthenticateSessionAsync(
        string pluginName,
        string pluginDeveloper,
        string authenticationToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pluginName))
            throw new ArgumentException("Plugin name is required.", nameof(pluginName));
        if (string.IsNullOrWhiteSpace(pluginDeveloper))
            throw new ArgumentException("Plugin developer is required.", nameof(pluginDeveloper));
        if (string.IsNullOrWhiteSpace(authenticationToken))
            throw new ArgumentException("Authentication token is required.", nameof(authenticationToken));

        var payload = new VtubeStudioAuthenticationRequestData
        {
            PluginName = pluginName.Trim(),
            PluginDeveloper = pluginDeveloper.Trim(),
            AuthenticationToken = authenticationToken.Trim()
        };

        var response = await SendRequestAsync("AuthenticationRequest", payload, cancellationToken).ConfigureAwait(false);
        ThrowIfApiError(response);

        if (!string.Equals(response.MessageType, "AuthenticationResponse", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected response type '{response.MessageType}' for AuthenticationRequest.");

        var data = response.DeserializeData<VtubeStudioAuthenticationResponseData>(_jsonOptions)
            ?? new VtubeStudioAuthenticationResponseData();

        EmitInfo(
            "AuthResponse",
            "Processed VTube Studio authentication response.",
            $"authenticated={data.Authenticated}, reason={data.Reason ?? "<none>"}");

        return data;
    }

    public async Task<VtubeStudioEventSubscriptionResponseData> SetEventSubscriptionAsync(
        string? eventName,
        bool subscribe,
        object? config,
        CancellationToken cancellationToken)
    {
        if (subscribe && string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Event name is required when subscribing.", nameof(eventName));

        var payload = new VtubeStudioEventSubscriptionRequestData
        {
            EventName = string.IsNullOrWhiteSpace(eventName) ? null : eventName.Trim(),
            Subscribe = subscribe,
            Config = config
        };

        var response = await SendRequestAsync("EventSubscriptionRequest", payload, cancellationToken).ConfigureAwait(false);
        ThrowIfApiError(response);

        if (!string.Equals(response.MessageType, "EventSubscriptionResponse", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected response type '{response.MessageType}' for EventSubscriptionRequest.");

        var data = response.DeserializeData<VtubeStudioEventSubscriptionResponseData>(_jsonOptions)
            ?? new VtubeStudioEventSubscriptionResponseData();

        EmitInfo(
            "EventSubscriptionUpdated",
            "Updated VTube Studio event subscription state.",
            $"subscribe={subscribe}, eventName={eventName ?? "<all>"}, subscribedCount={data.SubscribedEventCount}");

        return data;
    }

    public async Task<VtubeStudioCurrentModelData> GetCurrentModelAsync(CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync("CurrentModelRequest", data: null, cancellationToken).ConfigureAwait(false);
        ThrowIfApiError(response);

        if (!string.Equals(response.MessageType, "CurrentModelResponse", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected response type '{response.MessageType}' for CurrentModelRequest.");

        return response.DeserializeData<VtubeStudioCurrentModelData>(_jsonOptions) ?? new VtubeStudioCurrentModelData();
    }

    public async Task<VtubeStudioAvailableModelsData> GetAvailableModelsAsync(CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync("AvailableModelsRequest", data: null, cancellationToken).ConfigureAwait(false);
        ThrowIfApiError(response);

        if (!string.Equals(response.MessageType, "AvailableModelsResponse", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected response type '{response.MessageType}' for AvailableModelsRequest.");

        return response.DeserializeData<VtubeStudioAvailableModelsData>(_jsonOptions) ?? new VtubeStudioAvailableModelsData();
    }

    public async Task<VtubeStudioModelLoadResponseData> LoadModelAsync(string? modelId, CancellationToken cancellationToken)
    {
        await _modelLoadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var elapsed = DateTime.UtcNow - _lastModelLoadUtc;
            var cooldown = TimeSpan.FromSeconds(2);
            if (_lastModelLoadUtc != DateTime.MinValue && elapsed < cooldown)
            {
                var delay = cooldown - elapsed;
                EmitInfo("ModelLoadCooldown", "Delaying model load to respect VTube Studio cooldown.", $"delayMs={delay.TotalMilliseconds:0}");
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            var payload = new VtubeStudioModelLoadRequestData { ModelId = string.IsNullOrWhiteSpace(modelId) ? string.Empty : modelId.Trim() };
            var response = await SendRequestAsync("ModelLoadRequest", payload, cancellationToken).ConfigureAwait(false);
            ThrowIfApiError(response);

            if (!string.Equals(response.MessageType, "ModelLoadResponse", StringComparison.Ordinal))
                throw new InvalidOperationException($"Unexpected response type '{response.MessageType}' for ModelLoadRequest.");

            _lastModelLoadUtc = DateTime.UtcNow;
            return response.DeserializeData<VtubeStudioModelLoadResponseData>(_jsonOptions) ?? new VtubeStudioModelLoadResponseData();
        }
        finally
        {
            _modelLoadLock.Release();
        }
    }

    public async Task<VtubeStudioHotkeysData> GetHotkeysAsync(string? modelId, string? live2DItemFileName, CancellationToken cancellationToken)
    {
        var payload = new VtubeStudioHotkeysRequestData
        {
            ModelId = string.IsNullOrWhiteSpace(modelId) ? null : modelId.Trim(),
            Live2DItemFileName = string.IsNullOrWhiteSpace(live2DItemFileName) ? null : live2DItemFileName.Trim()
        };

        var response = await SendRequestAsync("HotkeysInCurrentModelRequest", payload, cancellationToken).ConfigureAwait(false);
        ThrowIfApiError(response);

        if (!string.Equals(response.MessageType, "HotkeysInCurrentModelResponse", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected response type '{response.MessageType}' for HotkeysInCurrentModelRequest.");

        return response.DeserializeData<VtubeStudioHotkeysData>(_jsonOptions) ?? new VtubeStudioHotkeysData();
    }

    public async Task<VtubeStudioHotkeyTriggerResponseData> TriggerHotkeyAsync(string hotkeyId, string? itemInstanceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(hotkeyId))
            throw new ArgumentException("Hotkey ID/name is required.", nameof(hotkeyId));

        var payload = new VtubeStudioHotkeyTriggerRequestData
        {
            HotkeyId = hotkeyId.Trim(),
            ItemInstanceId = string.IsNullOrWhiteSpace(itemInstanceId) ? null : itemInstanceId.Trim()
        };

        var response = await SendRequestAsync("HotkeyTriggerRequest", payload, cancellationToken).ConfigureAwait(false);
        ThrowIfApiError(response);

        if (!string.Equals(response.MessageType, "HotkeyTriggerResponse", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected response type '{response.MessageType}' for HotkeyTriggerRequest.");

        return response.DeserializeData<VtubeStudioHotkeyTriggerResponseData>(_jsonOptions) ?? new VtubeStudioHotkeyTriggerResponseData();
    }

    public async Task<VtubeStudioExpressionStateData> GetExpressionStateAsync(bool details, string? expressionFile, CancellationToken cancellationToken)
    {
        var payload = new VtubeStudioExpressionStateRequestData
        {
            Details = details,
            ExpressionFile = string.IsNullOrWhiteSpace(expressionFile) ? string.Empty : expressionFile.Trim()
        };

        var response = await SendRequestAsync("ExpressionStateRequest", payload, cancellationToken).ConfigureAwait(false);
        ThrowIfApiError(response);

        if (!string.Equals(response.MessageType, "ExpressionStateResponse", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected response type '{response.MessageType}' for ExpressionStateRequest.");

        return response.DeserializeData<VtubeStudioExpressionStateData>(_jsonOptions) ?? new VtubeStudioExpressionStateData();
    }

    public async Task SetExpressionStateAsync(string expressionFile, bool active, double fadeTime, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expressionFile))
            throw new ArgumentException("Expression file is required.", nameof(expressionFile));

        var payload = new VtubeStudioExpressionActivationRequestData
        {
            ExpressionFile = expressionFile.Trim(),
            Active = active,
            FadeTime = fadeTime
        };

        var response = await SendRequestAsync("ExpressionActivationRequest", payload, cancellationToken).ConfigureAwait(false);
        ThrowIfApiError(response);

        if (!string.Equals(response.MessageType, "ExpressionActivationResponse", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected response type '{response.MessageType}' for ExpressionActivationRequest.");
    }

    public async Task<VtubeStudioParameterValueData> GetParameterValueAsync(string parameterName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
            throw new ArgumentException("Parameter name is required.", nameof(parameterName));

        var payload = new VtubeStudioParameterValueRequestData
        {
            Name = parameterName.Trim()
        };

        var response = await SendRequestAsync("ParameterValueRequest", payload, cancellationToken).ConfigureAwait(false);
        ThrowIfApiError(response);

        if (!string.Equals(response.MessageType, "ParameterValueResponse", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected response type '{response.MessageType}' for ParameterValueRequest.");

        return response.DeserializeData<VtubeStudioParameterValueData>(_jsonOptions) ?? new VtubeStudioParameterValueData();
    }

    public async Task InjectParameterDataAsync(VtubeStudioInjectParameterDataRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.ParameterValues is null || request.ParameterValues.Count == 0)
            throw new ArgumentException("At least one parameter value is required.", nameof(request));

        var response = await SendRequestAsync("InjectParameterDataRequest", request, cancellationToken).ConfigureAwait(false);
        ThrowIfApiError(response);

        if (!string.Equals(response.MessageType, "InjectParameterDataResponse", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected response type '{response.MessageType}' for InjectParameterDataRequest.");
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _connectionLock.Dispose();
        _sendLock.Dispose();
        _modelLoadLock.Dispose();
    }

    private async Task<VtubeStudioResponseEnvelope> SendRequestAsync(string messageType, object? data, CancellationToken cancellationToken)
    {
        if (!IsConnected || _socket is null)
            throw new InvalidOperationException("VTube Studio websocket is not connected.");

        var requestId = $"vts-{Interlocked.Increment(ref _requestSequence)}";
        var request = new VtubeStudioRequestEnvelope
        {
            RequestId = requestId,
            MessageType = messageType,
            Data = data
        };

        var payload = JsonSerializer.Serialize(request, _jsonOptions);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var pending = new TaskCompletionSource<VtubeStudioResponseEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pending.TryAdd(requestId, pending))
            throw new InvalidOperationException($"Duplicate VTube Studio request id '{requestId}'.");

        using var cancelRegistration = cancellationToken.Register(() =>
        {
            if (_pending.TryRemove(requestId, out var cancelled))
                cancelled.TrySetCanceled(cancellationToken);
        });

        try
        {
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(
                    new ArraySegment<byte>(payloadBytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }

            return await pending.Task.ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(requestId, out _);
            throw;
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream(8192);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    EmitInfo("RemoteClose", "VTube Studio closed the websocket connection.");
                    break;
                }

                stream.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                    continue;

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    stream.SetLength(0);
                    EmitWarning("UnexpectedMessageType", "Received non-text websocket message from VTube Studio.");
                    continue;
                }

                var payload = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
                stream.SetLength(0);
                HandleIncomingMessage(payload);
            }
        }
        catch (OperationCanceledException)
        {
            EmitInfo("ReceiveLoopCancelled", "VTube Studio receive loop canceled.");
        }
        catch (WebSocketException ex)
        {
            EmitError("WebSocketError", "VTube Studio websocket receive loop failed.", ex.Message);
        }
        catch (Exception ex)
        {
            EmitError("ReceiveLoopError", "Unexpected error in VTube Studio receive loop.", ex.Message);
        }
        finally
        {
            FailAllPending("VTube Studio websocket closed before response was received.");
        }
    }

    private void HandleIncomingMessage(string payload)
    {
        VtubeStudioResponseEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<VtubeStudioResponseEnvelope>(payload, _jsonOptions);
        }
        catch (Exception ex)
        {
            EmitWarning("InvalidJson", "Failed to parse VTube Studio response payload.", ex.Message);
            return;
        }

        if (envelope is null)
        {
            EmitWarning("EmptyPayload", "Received an empty VTube Studio response payload.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(envelope.RequestId) && _pending.TryRemove(envelope.RequestId, out var pending))
        {
            pending.TrySetResult(envelope);
            return;
        }

        if (TryEmitApiEvent(envelope))
            return;

        EmitInfo("UnmatchedMessage", "Received VTube Studio message with no matching pending request.", envelope.MessageType);
    }

    private bool TryEmitApiEvent(VtubeStudioResponseEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.MessageType))
            return false;

        if (string.Equals(envelope.MessageType, "APIError", StringComparison.Ordinal) ||
            envelope.MessageType.EndsWith("Response", StringComparison.Ordinal))
            return false;

        var apiEvent = new VtubeStudioApiEventMessage
        {
            EventName = envelope.MessageType,
            RequestId = envelope.RequestId,
            ApiTimestamp = envelope.Timestamp,
            ReceivedAtUtc = DateTime.UtcNow,
            Data = envelope.Data
        };

        try
        {
            ApiEventReceived?.Invoke(apiEvent);
        }
        catch
        {
            // Observer failures must not break receive loop.
        }

        return true;
    }

    private void ThrowIfApiError(VtubeStudioResponseEnvelope response)
    {
        if (!string.Equals(response.MessageType, "APIError", StringComparison.Ordinal))
            return;

        var apiError = response.DeserializeData<VtubeStudioApiErrorData>(_jsonOptions);
        var errorId = apiError?.ErrorId ?? -1;
        var message = apiError?.Message ?? "Unknown VTube Studio API error.";
        throw new VtubeStudioApiException(errorId, message);
    }

    private async Task DisconnectInternalAsync()
    {
        var socket = _socket;
        var cts = _receiveLoopCts;
        var receiveTask = _receiveLoopTask;

        _socket = null;
        _receiveLoopCts = null;
        _receiveLoopTask = null;
        Endpoint = null;

        cts?.Cancel();

        if (socket is not null)
        {
            try
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Nyautomator disconnect",
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                EmitWarning("DisconnectWarning", "Error while closing VTube Studio websocket.", ex.Message);
            }
            finally
            {
                socket.Dispose();
            }
        }

        if (receiveTask is not null)
        {
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch
            {
                // Receive loop exceptions are logged in receive loop itself.
            }
        }

        cts?.Dispose();
        FailAllPending("VTube Studio websocket disconnected.");
    }

    private void FailAllPending(string message)
    {
        foreach (var pair in _pending)
        {
            if (_pending.TryRemove(pair.Key, out var pending))
                pending.TrySetException(new InvalidOperationException(message));
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
            // Observer errors must not break client flow.
        }
    }
}
