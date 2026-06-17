using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyautomator.VTubeStudio;

public static class VtubeStudioProtocol
{
    public const string ApiName = "VTubeStudioPublicAPI";
    public const string ApiVersion = "1.0";
}

public sealed class VtubeStudioRequestEnvelope
{
    [JsonPropertyName("apiName")]
    public string ApiName { get; set; } = VtubeStudioProtocol.ApiName;

    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; set; } = VtubeStudioProtocol.ApiVersion;

    [JsonPropertyName("requestID")]
    public string? RequestId { get; set; }

    [JsonPropertyName("messageType")]
    public string MessageType { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

public sealed class VtubeStudioResponseEnvelope
{
    [JsonPropertyName("apiName")]
    public string? ApiName { get; set; }

    [JsonPropertyName("apiVersion")]
    public string? ApiVersion { get; set; }

    [JsonPropertyName("requestID")]
    public string? RequestId { get; set; }

    [JsonPropertyName("messageType")]
    public string? MessageType { get; set; }

    [JsonPropertyName("timestamp")]
    public long? Timestamp { get; set; }

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public T? DeserializeData<T>(JsonSerializerOptions options)
    {
        if (Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return default;

        return Data.Deserialize<T>(options);
    }
}

public sealed class VtubeStudioApiStateData
{
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("vTubeStudioVersion")]
    public string? VTubeStudioVersion { get; set; }

    [JsonPropertyName("currentSessionAuthenticated")]
    public bool CurrentSessionAuthenticated { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class VtubeStudioAuthenticationTokenRequestData
{
    [JsonPropertyName("pluginName")]
    public string PluginName { get; set; } = string.Empty;

    [JsonPropertyName("pluginDeveloper")]
    public string PluginDeveloper { get; set; } = string.Empty;

    [JsonPropertyName("pluginIcon")]
    public string? PluginIcon { get; set; }
}

public sealed class VtubeStudioAuthenticationTokenResponseData
{
    [JsonPropertyName("authenticationToken")]
    public string? AuthenticationToken { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class VtubeStudioAuthenticationRequestData
{
    [JsonPropertyName("pluginName")]
    public string PluginName { get; set; } = string.Empty;

    [JsonPropertyName("pluginDeveloper")]
    public string PluginDeveloper { get; set; } = string.Empty;

    [JsonPropertyName("authenticationToken")]
    public string AuthenticationToken { get; set; } = string.Empty;
}

public sealed class VtubeStudioAuthenticationResponseData
{
    [JsonPropertyName("authenticated")]
    public bool Authenticated { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class VtubeStudioEventSubscriptionRequestData
{
    [JsonPropertyName("eventName")]
    public string? EventName { get; set; }

    [JsonPropertyName("subscribe")]
    public bool Subscribe { get; set; }

    [JsonPropertyName("config")]
    public object? Config { get; set; }
}

public sealed class VtubeStudioEventSubscriptionResponseData
{
    [JsonPropertyName("subscribedEventCount")]
    public int SubscribedEventCount { get; set; }

    [JsonPropertyName("subscribedEvents")]
    public List<string> SubscribedEvents { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class VtubeStudioApiEventMessage
{
    public string EventName { get; init; } = string.Empty;
    public string? RequestId { get; init; }
    public long? ApiTimestamp { get; init; }
    public DateTime ReceivedAtUtc { get; init; } = DateTime.UtcNow;
    public JsonElement Data { get; init; }
}

public sealed class VtubeStudioApiErrorData
{
    [JsonPropertyName("errorID")]
    public int ErrorId { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public enum VtubeStudioConnectionState
{
    Disconnected,
    ConnectedUnauthenticated,
    Authenticated,
    Error
}

public sealed class VtubeStudioStatus
{
    public VtubeStudioConnectionState State { get; init; } = VtubeStudioConnectionState.Disconnected;
    public bool IsConnected { get; init; }
    public bool IsAuthenticated { get; init; }
    public bool HasStoredToken { get; init; }
    public string? Endpoint { get; init; }
    public string? PluginName { get; init; }
    public string? PluginDeveloper { get; init; }
    public string? VTubeStudioVersion { get; init; }
    public string? LastError { get; init; }
    public DateTime? LastAuthenticatedUtc { get; init; }
    public bool EventSubscriptionActive { get; init; }
    public DateTime? LastEventReceivedUtc { get; init; }
    public DateTime? LastSubscriptionAttemptUtc { get; init; }
    public string? LastSubscriptionError { get; init; }
    public int ReceivedEventCount { get; init; }
    public List<string> SubscribedEvents { get; init; } = [];
    public DateTime? LastForwardedEventUtc { get; init; }
    public int ForwardedEventCount { get; init; }
    public int ThrottledEventCount { get; init; }
    public DateTime UpdatedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class VtubeStudioOperationResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? Message { get; init; }
    public VtubeStudioStatus Status { get; init; } = new();

    public static VtubeStudioOperationResult CreateSuccess(VtubeStudioStatus status, string? message = null)
        => new() { Success = true, Message = message, Status = status };

    public static VtubeStudioOperationResult CreateFailure(string error, VtubeStudioStatus status)
        => new() { Success = false, Error = error, Status = status };
}

public sealed class VtubeStudioDataResult<T>
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? Message { get; init; }
    public VtubeStudioStatus Status { get; init; } = new();
    public T? Data { get; init; }

    public static VtubeStudioDataResult<T> CreateSuccess(T? data, VtubeStudioStatus status, string? message = null)
        => new() { Success = true, Data = data, Status = status, Message = message };

    public static VtubeStudioDataResult<T> CreateFailure(string error, VtubeStudioStatus status)
        => new() { Success = false, Error = error, Status = status };
}

public sealed class VtubeStudioCurrentModelData
{
    [JsonPropertyName("modelLoaded")]
    public bool ModelLoaded { get; set; }

    [JsonPropertyName("modelName")]
    public string? ModelName { get; set; }

    [JsonPropertyName("modelID")]
    public string? ModelId { get; set; }

    [JsonPropertyName("vtsModelName")]
    public string? VtsModelName { get; set; }

    [JsonPropertyName("vtsModelIconName")]
    public string? VtsModelIconName { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class VtubeStudioAvailableModelsData
{
    [JsonPropertyName("numberOfModels")]
    public int NumberOfModels { get; set; }

    [JsonPropertyName("availableModels")]
    public List<VtubeStudioModelInfo> AvailableModels { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class VtubeStudioModelInfo
{
    [JsonPropertyName("modelLoaded")]
    public bool ModelLoaded { get; set; }

    [JsonPropertyName("modelName")]
    public string? ModelName { get; set; }

    [JsonPropertyName("modelID")]
    public string? ModelId { get; set; }

    [JsonPropertyName("vtsModelName")]
    public string? VtsModelName { get; set; }

    [JsonPropertyName("vtsModelIconName")]
    public string? VtsModelIconName { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class VtubeStudioModelLoadRequestData
{
    [JsonPropertyName("modelID")]
    public string? ModelId { get; set; }
}

public sealed class VtubeStudioModelLoadResponseData
{
    [JsonPropertyName("modelID")]
    public string? ModelId { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class VtubeStudioHotkeysRequestData
{
    [JsonPropertyName("modelID")]
    public string? ModelId { get; set; }

    [JsonPropertyName("live2DItemFileName")]
    public string? Live2DItemFileName { get; set; }
}

public sealed class VtubeStudioHotkeysData
{
    [JsonPropertyName("modelLoaded")]
    public bool ModelLoaded { get; set; }

    [JsonPropertyName("modelName")]
    public string? ModelName { get; set; }

    [JsonPropertyName("modelID")]
    public string? ModelId { get; set; }

    [JsonPropertyName("availableHotkeys")]
    public List<VtubeStudioHotkeyInfo> AvailableHotkeys { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class VtubeStudioHotkeyInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("hotkeyID")]
    public string? HotkeyId { get; set; }

    [JsonPropertyName("onScreenButtonID")]
    public int? OnScreenButtonId { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class VtubeStudioHotkeyTriggerRequestData
{
    [JsonPropertyName("hotkeyID")]
    public string HotkeyId { get; set; } = string.Empty;

    [JsonPropertyName("itemInstanceID")]
    public string? ItemInstanceId { get; set; }
}

public sealed class VtubeStudioHotkeyTriggerResponseData
{
    [JsonPropertyName("hotkeyID")]
    public string? HotkeyId { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class VtubeStudioExpressionStateRequestData
{
    [JsonPropertyName("details")]
    public bool Details { get; set; } = true;

    [JsonPropertyName("expressionFile")]
    public string? ExpressionFile { get; set; }
}

public sealed class VtubeStudioExpressionStateData
{
    [JsonPropertyName("modelLoaded")]
    public bool ModelLoaded { get; set; }

    [JsonPropertyName("modelName")]
    public string? ModelName { get; set; }

    [JsonPropertyName("modelID")]
    public string? ModelId { get; set; }

    [JsonPropertyName("expressions")]
    public List<VtubeStudioExpressionInfo> Expressions { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class VtubeStudioExpressionInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("secondsRemaining")]
    public double SecondsRemaining { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class VtubeStudioExpressionActivationRequestData
{
    [JsonPropertyName("expressionFile")]
    public string ExpressionFile { get; set; } = string.Empty;

    [JsonPropertyName("fadeTime")]
    public double FadeTime { get; set; } = 0.25d;

    [JsonPropertyName("active")]
    public bool Active { get; set; }
}

public sealed class VtubeStudioParameterValueRequestData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class VtubeStudioParameterValueData
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("addedBy")]
    public string? AddedBy { get; set; }

    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("min")]
    public double Min { get; set; }

    [JsonPropertyName("max")]
    public double Max { get; set; }

    [JsonPropertyName("defaultValue")]
    public double DefaultValue { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class VtubeStudioInjectParameterDataRequest
{
    [JsonPropertyName("faceFound")]
    public bool? FaceFound { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("parameterValues")]
    public List<VtubeStudioInjectParameterValue> ParameterValues { get; set; } = [];
}

public sealed class VtubeStudioInjectParameterValue
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("weight")]
    public double? Weight { get; set; }
}

public sealed record VtubeStudioLogEntry(
    DateTime TimestampUtc,
    string Level,
    string EventName,
    string Message,
    string? Detail = null);
