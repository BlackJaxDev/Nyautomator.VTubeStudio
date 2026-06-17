using System;
using System.ComponentModel;

namespace NyautomatorUI.Server.Automation;

[AutomationOutputs("out", "value", "min", "max", "default", "name", "addedby", "exists")]
public sealed class VtubeStudioParameterNode : ValueNodeType
{
    public override string Id => "Value.VTubeStudio.Parameter";
    public override string DisplayName => "VTube Studio: Parameter";
    public override string Description => "Reads a single VTube Studio parameter value by parameter name.";

    [Description("Parameter name to read.")]
    [OptionsProvider(typeof(VtubeStudioParameterOptionsProvider))]
    public string ParameterName { get; set; } = string.Empty;

    [Description("Fallback value returned when parameter lookup fails.")]
    public double? DefaultValue { get; set; }

    public override object? ComputeValue(string? outputHandle)
    {
        var getter = VtubeStudioAutomationHost.GetParameter;
        if (getter is null)
            return ResolveFallback(outputHandle);

        var parameterName = ParseOptionValue(ParameterName);
        if (string.IsNullOrWhiteSpace(parameterName))
            return ResolveFallback(outputHandle);

        var snapshot = getter(parameterName, CancellationToken.None).GetAwaiter().GetResult();
        var handle = NormalizeHandle(outputHandle);

        if (snapshot is null)
            return ResolveFallback(handle);

        return handle switch
        {
            "value" or "out" or "" => snapshot.Value,
            "min" => snapshot.Min,
            "max" => snapshot.Max,
            "default" => snapshot.DefaultValue,
            "name" => snapshot.Name,
            "addedby" => snapshot.AddedBy,
            "exists" => true,
            _ => snapshot.Value
        };
    }

    public override Type? GetOutputType(string? outputHandle)
    {
        var handle = NormalizeHandle(outputHandle);
        return ResolveOutputTypeToken(handle switch
        {
            "name" or "addedby" => "string",
            "exists" => "boolean",
            "value" or "out" or "" or "min" or "max" or "default" => "float64",
            _ => null
        }, fallbackToObject: false);
    }

    private object? ResolveFallback(string? outputHandle)
    {
        var handle = NormalizeHandle(outputHandle);
        return handle switch
        {
            "exists" => false,
            "value" or "out" or "" => DefaultValue,
            _ => null
        };
    }

    private static string? ParseOptionValue(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        var separator = trimmed.IndexOf('|');
        if (separator <= 0)
            return trimmed;

        return trimmed[..separator].Trim();
    }

    private static string NormalizeHandle(string? handle)
    {
        var normalized = AutomationValueHelper.NormalizeValueHandle(handle);
        return string.IsNullOrEmpty(normalized) ? string.Empty : normalized.ToLowerInvariant();
    }
}
