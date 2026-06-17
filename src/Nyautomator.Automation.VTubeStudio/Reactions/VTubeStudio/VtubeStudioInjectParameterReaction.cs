using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace NyautomatorUI.Server.Automation;

public sealed class VtubeStudioInjectParameterReaction : VtubeStudioReactionBase
{
    public override string Id => "Action.VTubeStudio.InjectParameter";
    public override string DisplayName => "VTube Studio: Inject Parameter";
    public override string Description => "Injects a parameter value into VTube Studio using InjectParameterDataRequest.";

    [Description("Parameter ID to inject (for example FaceAngleX or ParamMouthOpenY).")]
    [OptionsProvider(typeof(VtubeStudioParameterOptionsProvider))]
    public string ParameterId { get; set; } = string.Empty;

    [Description("Injected parameter value.")]
    public double Value { get; set; }

    [Description("Optional blend weight. Leave at 1 for default blending.")]
    public double Weight { get; set; } = 1d;

    [Description("Injection mode. Defaults to set.")]
    [OptionsProvider(typeof(VtubeStudioInjectModeOptionsProvider))]
    public string Mode { get; set; } = "set";

    [Description("Optional face-found override.")]
    public bool? FaceFound { get; set; }

    protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        var inject = VtubeStudioAutomationHost.InjectParameter;
        if (inject is null)
        {
            Log("VTube Studio inject-parameter delegate is not available.");
            return;
        }

        var parameterId = ParseOptionValue(ParameterId);
        if (string.IsNullOrWhiteSpace(parameterId))
            return;

        var mode = string.IsNullOrWhiteSpace(Mode) ? "set" : Mode.Trim().ToLowerInvariant();
        var weight = double.IsFinite(Weight) ? Weight : 1d;

        var command = new VtubeStudioInjectParameterCommand(
            mode,
            FaceFound,
            [new VtubeStudioInjectParameterValueCommand(parameterId, Value, weight)]);

        var success = await inject(command, cancellationToken).ConfigureAwait(false);
        IsSuccessful(success, "Inject parameter");
    }
}
