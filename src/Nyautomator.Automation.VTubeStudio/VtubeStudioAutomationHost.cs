namespace NyautomatorUI.Server.Automation;

public static class VtubeStudioAutomationHost
{
    public static Func<CancellationToken, Task<IReadOnlyList<VtubeStudioModelOption>>>? GetModels { get; set; }
    public static Func<string?, CancellationToken, Task<IReadOnlyList<VtubeStudioHotkeyOption>>>? GetHotkeys { get; set; }
    public static Func<CancellationToken, Task<IReadOnlyList<VtubeStudioExpressionOption>>>? GetExpressions { get; set; }
    public static Func<CancellationToken, Task<VtubeStudioActiveModelSnapshot?>>? GetActiveModel { get; set; }
    public static Func<string, CancellationToken, Task<VtubeStudioParameterSnapshot?>>? GetParameter { get; set; }
    public static Func<string, string?, CancellationToken, Task<bool>>? TriggerHotkey { get; set; }
    public static Func<string?, CancellationToken, Task<bool>>? LoadModel { get; set; }
    public static Func<string, bool, double, CancellationToken, Task<bool>>? SetExpression { get; set; }
    public static Func<VtubeStudioInjectParameterCommand, CancellationToken, Task<bool>>? InjectParameter { get; set; }
}

public sealed record VtubeStudioModelOption(
    string ModelId,
    string ModelName,
    bool IsLoaded);

public sealed record VtubeStudioHotkeyOption(
    string HotkeyId,
    string Name,
    string? Type,
    string? Description);

public sealed record VtubeStudioExpressionOption(
    string File,
    string Name,
    bool Active);

public sealed record VtubeStudioActiveModelSnapshot(
    bool ModelLoaded,
    string? ModelId,
    string? ModelName,
    string? VtsModelName,
    string? VtsModelIconName);

public sealed record VtubeStudioParameterSnapshot(
    string Name,
    double Value,
    double Min,
    double Max,
    double DefaultValue,
    string? AddedBy);

public sealed record VtubeStudioInjectParameterValueCommand(
    string Id,
    double Value,
    double? Weight);

public sealed record VtubeStudioInjectParameterCommand(
    string? Mode,
    bool? FaceFound,
    IReadOnlyList<VtubeStudioInjectParameterValueCommand> ParameterValues);
