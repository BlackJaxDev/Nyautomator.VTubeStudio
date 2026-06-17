using System.Text.Json.Serialization;
using Nyautomator.VTubeStudio;

namespace Nyautomator.Modules.VTubeStudio;

public sealed class VtubeStudioModuleOptions
{
    public bool? Enabled { get; set; }
    public bool? AutoReconnect { get; set; }
    public string? ServerUrl { get; set; }
    public string? PluginName { get; set; }
    public string? PluginDeveloper { get; set; }

    [JsonIgnore]
    public bool EffectiveEnabled => Enabled ?? false;

    [JsonIgnore]
    public bool EffectiveAutoReconnect => AutoReconnect ?? true;

    public static VtubeStudioModuleOptions CreateDefault()
        => new()
        {
            Enabled = false,
            AutoReconnect = true,
            ServerUrl = "ws://localhost:8001",
            PluginName = "Nyautomator",
            PluginDeveloper = "BlackJaxDev"
        };

    public VtubeStudioConnectionOptions ToConnectionOptions()
        => new()
        {
            Enabled = Enabled ?? false,
            AutoReconnect = AutoReconnect ?? true,
            ServerUrl = First(ServerUrl, "ws://localhost:8001")!,
            PluginName = First(PluginName, "Nyautomator")!,
            PluginDeveloper = First(PluginDeveloper, "BlackJaxDev")!
        };

    private static string? First(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
