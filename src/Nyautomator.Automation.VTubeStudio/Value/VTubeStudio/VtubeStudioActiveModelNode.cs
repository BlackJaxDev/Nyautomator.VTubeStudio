using System;

namespace NyautomatorUI.Server.Automation;

[AutomationOutputs("out", "modelloaded", "modelid", "modelname", "vtsmodelname", "vtsmodeliconname")]
public sealed class VtubeStudioActiveModelNode : ValueNodeType
{
    public override string Id => "Value.VTubeStudio.ActiveModel";
    public override string DisplayName => "VTube Studio: Active Model";
    public override string Description => "Returns details for the currently loaded VTube Studio model.";

    public override object? ComputeValue(string? outputHandle)
    {
        var getter = VtubeStudioAutomationHost.GetActiveModel;
        if (getter is null)
            return null;

        var snapshot = getter(CancellationToken.None).GetAwaiter().GetResult();
        var handle = NormalizeHandle(outputHandle);

        if (snapshot is null)
            return handle == "modelloaded" ? false : null;

        return handle switch
        {
            "modelloaded" => snapshot.ModelLoaded,
            "modelid" => snapshot.ModelId,
            "modelname" => snapshot.ModelName,
            "vtsmodelname" => snapshot.VtsModelName,
            "vtsmodeliconname" => snapshot.VtsModelIconName,
            _ => snapshot.ModelId
        };
    }

    public override Type? GetOutputType(string? outputHandle)
    {
        var handle = NormalizeHandle(outputHandle);
        return ResolveOutputTypeToken(handle switch
        {
            "modelloaded" => "boolean",
            "modelid" or "modelname" or "vtsmodelname" or "vtsmodeliconname" => "string",
            _ => null
        }, fallbackToObject: false);
    }

    private static string NormalizeHandle(string? handle)
    {
        var normalized = AutomationValueHelper.NormalizeValueHandle(handle);
        return string.IsNullOrEmpty(normalized) ? string.Empty : normalized.ToLowerInvariant();
    }
}
