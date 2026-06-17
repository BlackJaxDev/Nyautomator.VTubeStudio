namespace Nyautomator.VTubeStudio;

public sealed class VtubeStudioConnectionOptions
{
    public bool Enabled { get; set; }
    public bool AutoReconnect { get; set; } = true;
    public string ServerUrl { get; set; } = "ws://localhost:8001";
    public string PluginName { get; set; } = "Nyautomator";
    public string PluginDeveloper { get; set; } = "BlackJaxDev";
}
