using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NyautomatorUI.Server.Automation;

public sealed class VtubeStudioModelOptionsProvider : IStringOptionsProvider
{
    public IEnumerable<string> GetOptions()
    {
        var getter = VtubeStudioAutomationHost.GetModels;
        if (getter is null)
            return Array.Empty<string>();

        try
        {
            var models = getter(CancellationToken.None).GetAwaiter().GetResult();
            if (models is null || models.Count == 0)
                return Array.Empty<string>();

            return models
                .OrderBy(model => model.ModelName, StringComparer.OrdinalIgnoreCase)
                .Select(model => $"{model.ModelId}|{model.ModelName}")
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}

public sealed class VtubeStudioHotkeyOptionsProvider : IStringOptionsProvider
{
    public IEnumerable<string> GetOptions()
    {
        var getter = VtubeStudioAutomationHost.GetHotkeys;
        if (getter is null)
            return Array.Empty<string>();

        try
        {
            var hotkeys = getter(null, CancellationToken.None).GetAwaiter().GetResult();
            if (hotkeys is null || hotkeys.Count == 0)
                return Array.Empty<string>();

            return hotkeys
                .OrderBy(hotkey => hotkey.Name, StringComparer.OrdinalIgnoreCase)
                .Select(hotkey => $"{hotkey.HotkeyId}|{hotkey.Name}")
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}

public sealed class VtubeStudioExpressionOptionsProvider : IStringOptionsProvider
{
    public IEnumerable<string> GetOptions()
    {
        var getter = VtubeStudioAutomationHost.GetExpressions;
        if (getter is null)
            return Array.Empty<string>();

        try
        {
            var expressions = getter(CancellationToken.None).GetAwaiter().GetResult();
            if (expressions is null || expressions.Count == 0)
                return Array.Empty<string>();

            return expressions
                .OrderBy(expression => expression.Name, StringComparer.OrdinalIgnoreCase)
                .Select(expression => $"{expression.File}|{expression.Name}")
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}

public sealed class VtubeStudioParameterOptionsProvider : IStringOptionsProvider
{
    private static readonly string[] FallbackParameters =
    [
        "FaceAngleX",
        "FaceAngleY",
        "FaceAngleZ",
        "FacePositionX",
        "FacePositionY",
        "MouthOpen",
        "EyeOpenLeft",
        "EyeOpenRight",
        "BrowLeftY",
        "BrowRightY"
    ];

    public IEnumerable<string> GetOptions() => FallbackParameters;
}

public sealed class VtubeStudioInjectModeOptionsProvider : IStringOptionsProvider
{
    public IEnumerable<string> GetOptions() => ["set", "add", "mix"];
}
