using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace NyautomatorUI.Server.Automation;

public sealed class VtubeStudioTriggerHotkeyReaction : VtubeStudioReactionBase
{
    public override string Id => "Action.VTubeStudio.TriggerHotkey";
    public override string DisplayName => "VTube Studio: Trigger Hotkey";
    public override string Description => "Triggers a VTube Studio hotkey by ID.";

    [Description("Hotkey ID to trigger. Supports dropdown options from the active model.")]
    [OptionsProvider(typeof(VtubeStudioHotkeyOptionsProvider))]
    public string HotkeyId { get; set; } = string.Empty;

    [Description("Optional item instance ID when triggering item hotkeys.")]
    public string? ItemInstanceId { get; set; }

    protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        var trigger = VtubeStudioAutomationHost.TriggerHotkey;
        if (trigger is null)
        {
            Log("VTube Studio trigger delegate is not available.");
            return;
        }

        var hotkeyId = ParseOptionValue(HotkeyId);
        if (string.IsNullOrWhiteSpace(hotkeyId))
            return;

        var itemInstanceId = string.IsNullOrWhiteSpace(ItemInstanceId) ? null : ItemInstanceId.Trim();
        var success = await trigger(hotkeyId, itemInstanceId, cancellationToken).ConfigureAwait(false);
        IsSuccessful(success, "Trigger hotkey");
    }
}
