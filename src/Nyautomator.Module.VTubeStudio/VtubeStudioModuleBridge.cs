using System.Net;
using System.Text;
using System.Text.Json;
using Nyautomator;
using Nyautomator.Module.Abstractions;
using Nyautomator.Runtime.Abstractions;
using Nyautomator.Runtime.Integrations.VTubeStudio;
using Nyautomator.VTubeStudio;
using NyautomatorUI.Server.Automation;
using ModuleAuthRequest = Nyautomator.Module.Abstractions.AuthenticatedIntegrationRequest;
using ModuleAuthResponse = Nyautomator.Module.Abstractions.AuthenticatedIntegrationResponse;

namespace Nyautomator.Modules.VTubeStudio;

public sealed class VtubeStudioModuleBridge(
    IModuleOptionsProvider options,
    IVtubeStudioIntegration vtubeStudio) : IModuleApiHandler, IAuthenticatedIntegrationAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IModuleOptionsProvider _options = options;
    private readonly IVtubeStudioIntegration _vtubeStudio = vtubeStudio;

    public string ModuleId => "vtubestudio";
    public string Id => "vtubestudio";

    public static VtubeStudioModuleOptions GetEffectiveOptions(IModuleOptionsProvider options)
    {
        var fallback = VtubeStudioModuleOptions.CreateDefault();
        var configured = options.Get("vtubestudio", new VtubeStudioModuleOptions());
        return new VtubeStudioModuleOptions
        {
            Enabled = configured.Enabled ?? fallback.Enabled,
            AutoReconnect = configured.AutoReconnect ?? fallback.AutoReconnect,
            ServerUrl = First(configured.ServerUrl, fallback.ServerUrl),
            PluginName = First(configured.PluginName, fallback.PluginName),
            PluginDeveloper = First(configured.PluginDeveloper, fallback.PluginDeveloper)
        };
    }

    public async Task<ModuleApiResponse> HandleAsync(ModuleApiRequest request, CancellationToken cancellationToken)
    {
        var path = NormalizePath(request.Path);
        try
        {
            return path.ToLowerInvariant() switch
            {
                "status" when IsGet(request) => await GetStatusAsync(cancellationToken).ConfigureAwait(false),
                "connect" when IsPost(request) => await ConnectAsync(cancellationToken).ConfigureAwait(false),
                "disconnect" when IsPost(request) => await DisconnectAsync(await ReadBodyAsync<VtubeStudioDisconnectRequest>(request.Body, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false),
                "auth/token/request" when IsPost(request) => await RequestAuthTokenAsync(await ReadBodyAsync<VtubeStudioAuthTokenRequest>(request.Body, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false),
                "auth/session" when IsPost(request) => await AuthenticateSessionAsync(cancellationToken).ConfigureAwait(false),
                "auth/clear" when IsPost(request) => await ClearAuthTokenAsync(cancellationToken).ConfigureAwait(false),
                "model/current" when IsGet(request) => await GetCurrentModelAsync(cancellationToken).ConfigureAwait(false),
                "models" when IsGet(request) => await GetModelsAsync(cancellationToken).ConfigureAwait(false),
                "model/load" when IsPost(request) => await LoadModelAsync(await ReadBodyAsync<VtubeStudioModelLoadRequest>(request.Body, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false),
                "hotkeys" when IsGet(request) => await GetHotkeysAsync(request, cancellationToken).ConfigureAwait(false),
                "hotkey/trigger" when IsPost(request) => await TriggerHotkeyAsync(await ReadBodyAsync<VtubeStudioHotkeyTriggerRequest>(request.Body, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false),
                "expressions" when IsGet(request) => await GetExpressionsAsync(request, cancellationToken).ConfigureAwait(false),
                "expression" when IsPost(request) => await SetExpressionAsync(await ReadBodyAsync<VtubeStudioSetExpressionRequest>(request.Body, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false),
                "expression/set" when IsPost(request) => await SetExpressionAsync(await ReadBodyAsync<VtubeStudioSetExpressionRequest>(request.Body, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false),
                "parameter" when IsGet(request) => await GetParameterAsync(request, cancellationToken).ConfigureAwait(false),
                "parameter/inject" when IsPost(request) => await InjectParameterAsync(await ReadBodyAsync<VtubeStudioInjectParameterRequest>(request.Body, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false),
                _ => NotFound($"VTube Studio module API path '/{path}' is not available.")
            };
        }
        catch (OperationCanceledException)
        {
            return Error("VTube Studio request cancelled or timed out.", 504);
        }
        catch (Exception ex)
        {
            return Error($"VTube Studio request failed: {ex.Message}", 500);
        }
    }

    public async Task<ModuleAuthResponse> SendAsync(ModuleAuthRequest request, CancellationToken cancellationToken)
    {
        var bodyBytes = string.IsNullOrEmpty(request.Body)
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(request.Body);
        await using var body = new MemoryStream(bodyBytes);
        var apiRequest = new ModuleApiRequest(
            ModuleId,
            request.Method,
            "/" + NormalizePath(request.Path),
            ParseQuery(request.Query),
            body,
            request.ContentType,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var response = await HandleAsync(apiRequest, cancellationToken).ConfigureAwait(false);
        return ToAuthResponse(response);
    }

    public void WireAutomationHost()
    {
        VtubeStudioAutomationHost.GetModels = async cancellationToken =>
        {
            await ConfigureAsync(cancellationToken).ConfigureAwait(false);
            var result = await _vtubeStudio.Auth.GetAvailableModelsAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Success || result.Data is null)
                return Array.Empty<VtubeStudioModelOption>();

            return result.Data.AvailableModels
                .Where(model => !string.IsNullOrWhiteSpace(model.ModelId))
                .Select(model => new VtubeStudioModelOption(
                    model.ModelId!,
                    string.IsNullOrWhiteSpace(model.ModelName) ? model.ModelId! : model.ModelName!,
                    model.ModelLoaded))
                .ToArray();
        };

        VtubeStudioAutomationHost.GetHotkeys = async (modelId, cancellationToken) =>
        {
            await ConfigureAsync(cancellationToken).ConfigureAwait(false);
            var result = await _vtubeStudio.Auth.GetHotkeysAsync(modelId, null, cancellationToken).ConfigureAwait(false);
            if (!result.Success || result.Data is null)
                return Array.Empty<VtubeStudioHotkeyOption>();

            return result.Data.AvailableHotkeys
                .Where(hotkey => !string.IsNullOrWhiteSpace(hotkey.HotkeyId))
                .Select(hotkey => new VtubeStudioHotkeyOption(
                    hotkey.HotkeyId!,
                    string.IsNullOrWhiteSpace(hotkey.Name) ? hotkey.HotkeyId! : hotkey.Name!,
                    hotkey.Type,
                    hotkey.Description))
                .ToArray();
        };

        VtubeStudioAutomationHost.GetExpressions = async cancellationToken =>
        {
            await ConfigureAsync(cancellationToken).ConfigureAwait(false);
            var result = await _vtubeStudio.Auth.GetExpressionStateAsync(details: true, expressionFile: null, cancellationToken).ConfigureAwait(false);
            if (!result.Success || result.Data is null)
                return Array.Empty<VtubeStudioExpressionOption>();

            return result.Data.Expressions
                .Where(expression => !string.IsNullOrWhiteSpace(expression.File))
                .Select(expression => new VtubeStudioExpressionOption(
                    expression.File!,
                    string.IsNullOrWhiteSpace(expression.Name) ? expression.File! : expression.Name!,
                    expression.Active))
                .ToArray();
        };

        VtubeStudioAutomationHost.GetActiveModel = async cancellationToken =>
        {
            await ConfigureAsync(cancellationToken).ConfigureAwait(false);
            var result = await _vtubeStudio.Auth.GetCurrentModelAsync(cancellationToken).ConfigureAwait(false);
            var data = result.Success ? result.Data : null;
            return data is null
                ? null
                : new VtubeStudioActiveModelSnapshot(
                    data.ModelLoaded,
                    data.ModelId,
                    data.ModelName,
                    data.VtsModelName,
                    data.VtsModelIconName);
        };

        VtubeStudioAutomationHost.GetParameter = async (name, cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            await ConfigureAsync(cancellationToken).ConfigureAwait(false);
            var result = await _vtubeStudio.Auth.GetParameterValueAsync(name.Trim(), cancellationToken).ConfigureAwait(false);
            var data = result.Success ? result.Data : null;
            return data is null
                ? null
                : new VtubeStudioParameterSnapshot(
                    data.Name ?? name.Trim(),
                    data.Value,
                    data.Min,
                    data.Max,
                    data.DefaultValue,
                    data.AddedBy);
        };

        VtubeStudioAutomationHost.TriggerHotkey = async (hotkeyId, itemInstanceId, cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(hotkeyId))
                return false;

            await ConfigureAsync(cancellationToken).ConfigureAwait(false);
            var result = await _vtubeStudio.Auth.TriggerHotkeyAsync(hotkeyId.Trim(), itemInstanceId, cancellationToken).ConfigureAwait(false);
            return result.Success;
        };

        VtubeStudioAutomationHost.LoadModel = async (modelId, cancellationToken) =>
        {
            await ConfigureAsync(cancellationToken).ConfigureAwait(false);
            var result = await _vtubeStudio.Auth.LoadModelAsync(modelId, cancellationToken).ConfigureAwait(false);
            return result.Success;
        };

        VtubeStudioAutomationHost.SetExpression = async (expressionFile, active, fadeTime, cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(expressionFile))
                return false;

            await ConfigureAsync(cancellationToken).ConfigureAwait(false);
            var result = await _vtubeStudio.Auth.SetExpressionStateAsync(expressionFile.Trim(), active, fadeTime, cancellationToken).ConfigureAwait(false);
            return result.Success;
        };

        VtubeStudioAutomationHost.InjectParameter = async (command, cancellationToken) =>
        {
            if (command is null || command.ParameterValues.Count == 0)
                return false;

            await ConfigureAsync(cancellationToken).ConfigureAwait(false);
            var request = new VtubeStudioInjectParameterDataRequest
            {
                FaceFound = command.FaceFound,
                Mode = command.Mode,
                ParameterValues = command.ParameterValues
                    .Where(value => !string.IsNullOrWhiteSpace(value.Id))
                    .Select(value => new VtubeStudioInjectParameterValue
                    {
                        Id = value.Id,
                        Value = value.Value,
                        Weight = value.Weight
                    })
                    .ToList()
            };

            if (request.ParameterValues.Count == 0)
                return false;

            var result = await _vtubeStudio.Auth.InjectParameterDataAsync(request, cancellationToken).ConfigureAwait(false);
            return result.Success;
        };
    }

    private Task ConfigureAsync(CancellationToken cancellationToken)
    {
        var config = GetEffectiveOptions(_options).ToConnectionOptions();
        return _vtubeStudio.ConfigureAsync(config, cancellationToken);
    }

    private async Task<ModuleApiResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        return ModuleApiResponse.Json(new
        {
            success = true,
            status = ToVtubeStudioDto(_vtubeStudio.GetStatusSnapshot())
        });
    }

    private async Task<ModuleApiResponse> ConnectAsync(CancellationToken cancellationToken)
    {
        await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        return ModuleApiResponse.Json(ToVtubeStudioResponse(
            await _vtubeStudio.Auth.ConnectAsync(cancellationToken).ConfigureAwait(false)));
    }

    private async Task<ModuleApiResponse> DisconnectAsync(VtubeStudioDisconnectRequest? request, CancellationToken cancellationToken)
    {
        await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        var clearStoredToken = request?.ClearStoredToken ?? false;
        return ModuleApiResponse.Json(ToVtubeStudioResponse(
            await _vtubeStudio.Auth.DisconnectAsync(clearStoredToken, cancellationToken).ConfigureAwait(false)));
    }

    private async Task<ModuleApiResponse> RequestAuthTokenAsync(VtubeStudioAuthTokenRequest? request, CancellationToken cancellationToken)
    {
        await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        return ModuleApiResponse.Json(ToVtubeStudioResponse(
            await _vtubeStudio.Auth.RequestAuthenticationTokenAsync(request?.PluginIconBase64, cancellationToken).ConfigureAwait(false)));
    }

    private async Task<ModuleApiResponse> AuthenticateSessionAsync(CancellationToken cancellationToken)
    {
        await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        return ModuleApiResponse.Json(ToVtubeStudioResponse(
            await _vtubeStudio.Auth.AuthenticateWithStoredTokenAsync(cancellationToken).ConfigureAwait(false)));
    }

    private async Task<ModuleApiResponse> ClearAuthTokenAsync(CancellationToken cancellationToken)
    {
        await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        _vtubeStudio.Auth.ClearStoredToken();
        return ModuleApiResponse.Json(new
        {
            success = true,
            message = "Cleared VTube Studio authentication token.",
            status = ToVtubeStudioDto(_vtubeStudio.GetStatusSnapshot())
        });
    }

    private async Task<ModuleApiResponse> GetCurrentModelAsync(CancellationToken cancellationToken)
    {
        await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        return ModuleApiResponse.Json(ToVtubeStudioDataResponse(
            await _vtubeStudio.Auth.GetCurrentModelAsync(cancellationToken).ConfigureAwait(false)));
    }

    private async Task<ModuleApiResponse> GetModelsAsync(CancellationToken cancellationToken)
    {
        await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        return ModuleApiResponse.Json(ToVtubeStudioDataResponse(
            await _vtubeStudio.Auth.GetAvailableModelsAsync(cancellationToken).ConfigureAwait(false)));
    }

    private async Task<ModuleApiResponse> LoadModelAsync(VtubeStudioModelLoadRequest? request, CancellationToken cancellationToken)
    {
        await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        return ModuleApiResponse.Json(ToVtubeStudioDataResponse(
            await _vtubeStudio.Auth.LoadModelAsync(request?.ModelId, cancellationToken).ConfigureAwait(false)));
    }

    private async Task<ModuleApiResponse> GetHotkeysAsync(ModuleApiRequest request, CancellationToken cancellationToken)
    {
        await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        return ModuleApiResponse.Json(ToVtubeStudioDataResponse(
            await _vtubeStudio.Auth.GetHotkeysAsync(QueryValue(request, "modelId"), QueryValue(request, "live2DItemFileName"), cancellationToken).ConfigureAwait(false)));
    }

    private async Task<ModuleApiResponse> TriggerHotkeyAsync(VtubeStudioHotkeyTriggerRequest? request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.HotkeyId))
            return Error("hotkeyId is required.", 400);

        await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        return ModuleApiResponse.Json(ToVtubeStudioDataResponse(
            await _vtubeStudio.Auth.TriggerHotkeyAsync(request.HotkeyId, request.ItemInstanceId, cancellationToken).ConfigureAwait(false)));
    }

    private async Task<ModuleApiResponse> GetExpressionsAsync(ModuleApiRequest request, CancellationToken cancellationToken)
    {
        await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        return ModuleApiResponse.Json(ToVtubeStudioDataResponse(
            await _vtubeStudio.Auth.GetExpressionStateAsync(ParseBooleanQuery(request, "details", defaultValue: true), QueryValue(request, "expressionFile"), cancellationToken).ConfigureAwait(false)));
    }

    private async Task<ModuleApiResponse> SetExpressionAsync(VtubeStudioSetExpressionRequest? request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.ExpressionFile))
            return Error("expressionFile is required.", 400);

        await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        return ModuleApiResponse.Json(ToVtubeStudioDataResponse(
            await _vtubeStudio.Auth.SetExpressionStateAsync(request.ExpressionFile, request.Active, request.FadeTime, cancellationToken).ConfigureAwait(false)));
    }

    private async Task<ModuleApiResponse> GetParameterAsync(ModuleApiRequest request, CancellationToken cancellationToken)
    {
        var name = QueryValue(request, "name");
        if (string.IsNullOrWhiteSpace(name))
            return Error("name is required.", 400);

        await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        return ModuleApiResponse.Json(ToVtubeStudioDataResponse(
            await _vtubeStudio.Auth.GetParameterValueAsync(name, cancellationToken).ConfigureAwait(false)));
    }

    private async Task<ModuleApiResponse> InjectParameterAsync(VtubeStudioInjectParameterRequest? request, CancellationToken cancellationToken)
    {
        if (request?.ParameterValues is null || request.ParameterValues.Count == 0)
            return Error("At least one parameter value is required.", 400);

        var payload = new VtubeStudioInjectParameterDataRequest
        {
            FaceFound = request.FaceFound,
            Mode = string.IsNullOrWhiteSpace(request.Mode) ? null : request.Mode,
            ParameterValues = request.ParameterValues
                .Where(static item => !string.IsNullOrWhiteSpace(item.Id))
                .Select(item => new VtubeStudioInjectParameterValue
                {
                    Id = item.Id!.Trim(),
                    Value = item.Value,
                    Weight = item.Weight
                })
                .ToList()
        };

        if (payload.ParameterValues.Count == 0)
            return Error("parameterValues must contain at least one valid id.", 400);

        await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        var result = await _vtubeStudio.Auth.InjectParameterDataAsync(payload, cancellationToken).ConfigureAwait(false);
        return ModuleApiResponse.Json(new
        {
            success = result.Success,
            message = result.Success
                ? "Parameter data injected. Re-send at least every 1000ms while controlling parameters."
                : result.Message,
            error = result.Error,
            status = ToVtubeStudioDto(result.Status),
            recommendedRefreshMs = 1000
        });
    }

    private static object ToVtubeStudioResponse(VtubeStudioOperationResult result)
        => new
        {
            success = result.Success,
            message = string.IsNullOrWhiteSpace(result.Message) ? null : result.Message,
            error = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error,
            status = ToVtubeStudioDto(result.Status)
        };

    private static object ToVtubeStudioDataResponse<T>(VtubeStudioDataResult<T> result)
        => new
        {
            success = result.Success,
            message = string.IsNullOrWhiteSpace(result.Message) ? null : result.Message,
            error = string.IsNullOrWhiteSpace(result.Error) ? null : result.Error,
            status = ToVtubeStudioDto(result.Status),
            data = result.Data
        };

    private static object ToVtubeStudioDto(VtubeStudioStatus status)
        => new
        {
            state = status.State.ToString(),
            isConnected = status.IsConnected,
            isAuthenticated = status.IsAuthenticated,
            hasStoredToken = status.HasStoredToken,
            endpoint = status.Endpoint,
            pluginName = status.PluginName,
            pluginDeveloper = status.PluginDeveloper,
            vtsVersion = status.VTubeStudioVersion,
            lastError = status.LastError,
            lastAuthenticatedUtc = status.LastAuthenticatedUtc,
            eventSubscriptionActive = status.EventSubscriptionActive,
            subscribedEvents = status.SubscribedEvents,
            lastSubscriptionAttemptUtc = status.LastSubscriptionAttemptUtc,
            lastSubscriptionError = status.LastSubscriptionError,
            lastEventReceivedUtc = status.LastEventReceivedUtc,
            receivedEventCount = status.ReceivedEventCount,
            lastForwardedEventUtc = status.LastForwardedEventUtc,
            forwardedEventCount = status.ForwardedEventCount,
            throttledEventCount = status.ThrottledEventCount,
            updatedAtUtc = status.UpdatedAtUtc
        };

    private static ModuleAuthResponse ToAuthResponse(ModuleApiResponse response)
        => new(
            Success: response.StatusCode is >= 200 and <= 299,
            StatusCode: response.StatusCode,
            Body: response.Body is null ? null : JsonSerializer.Serialize(response.Body, JsonOptions),
            Error: response.StatusCode is >= 200 and <= 299 ? null : "VTube Studio module request failed.",
            ContentType: response.ContentType ?? "application/json",
            Headers: response.Headers);

    private static ModuleApiResponse NotFound(string message)
        => ModuleApiResponse.Json(new { ok = false, success = false, error = message }, 404);

    private static ModuleApiResponse Error(string message, int statusCode)
        => ModuleApiResponse.Json(new { ok = false, success = false, error = message }, statusCode);

    private static string NormalizePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Trim('/');

    private static bool IsGet(ModuleApiRequest request)
        => IsMethod(request.Method, "GET");

    private static bool IsPost(ModuleApiRequest request)
        => IsMethod(request.Method, "POST");

    private static bool IsMethod(string? actual, string expected)
        => string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static async Task<T?> ReadBodyAsync<T>(Stream body, CancellationToken cancellationToken)
    {
        if (body is null || !body.CanRead)
            return default;

        return await JsonSerializer.DeserializeAsync<T>(body, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string?> ParseQuery(string? query)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return values;

        foreach (var part in query.Trim().TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var split = part.Split('=', 2);
            var name = WebUtility.UrlDecode(split[0]);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            values[name] = split.Length > 1 ? WebUtility.UrlDecode(split[1]) : string.Empty;
        }

        return values;
    }

    private static string? QueryValue(ModuleApiRequest request, string name)
        => request.Query.TryGetValue(name, out var value) ? value : null;

    private static bool ParseBooleanQuery(ModuleApiRequest request, string name, bool defaultValue)
        => bool.TryParse(QueryValue(request, name), out var value) ? value : defaultValue;

    private static string? First(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record VtubeStudioAuthTokenRequest(string? PluginIconBase64);
    private sealed record VtubeStudioDisconnectRequest(bool ClearStoredToken);
    private sealed record VtubeStudioModelLoadRequest(string? ModelId);
    private sealed record VtubeStudioHotkeyTriggerRequest(string? HotkeyId, string? ItemInstanceId);
    private sealed record VtubeStudioSetExpressionRequest(string? ExpressionFile, bool Active, double FadeTime = 0.25d);
    private sealed record VtubeStudioInjectParameterRequest(bool? FaceFound, string? Mode, List<VtubeStudioInjectParameterValueRequest> ParameterValues);
    private sealed record VtubeStudioInjectParameterValueRequest(string? Id, double Value, double? Weight);
}
