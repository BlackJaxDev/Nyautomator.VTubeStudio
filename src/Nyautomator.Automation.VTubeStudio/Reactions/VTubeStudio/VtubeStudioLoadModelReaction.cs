using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace NyautomatorUI.Server.Automation;

public sealed class VtubeStudioLoadModelReaction : VtubeStudioReactionBase
{
    public override string Id => "Action.VTubeStudio.LoadModel";
    public override string DisplayName => "VTube Studio: Load Model";
    public override string Description => "Loads a VTube Studio model by model ID.";

    [Description("Model ID to load. Supports dropdown options.")]
    [OptionsProvider(typeof(VtubeStudioModelOptionsProvider))]
    public string ModelId { get; set; } = string.Empty;

    protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        var load = VtubeStudioAutomationHost.LoadModel;
        if (load is null)
        {
            Log("VTube Studio load-model delegate is not available.");
            return;
        }

        var modelId = ParseOptionValue(ModelId);
        if (string.IsNullOrWhiteSpace(modelId))
            return;

        var success = await load(modelId, cancellationToken).ConfigureAwait(false);
        IsSuccessful(success, "Load model");
    }
}
