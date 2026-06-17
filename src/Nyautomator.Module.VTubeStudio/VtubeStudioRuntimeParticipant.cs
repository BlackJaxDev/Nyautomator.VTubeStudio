using Nyautomator;
using Nyautomator.Runtime.Abstractions;
using Nyautomator.Runtime.Integrations.VTubeStudio;

namespace Nyautomator.Modules.VTubeStudio;

public sealed class VtubeStudioRuntimeParticipant(
    IServiceProvider services,
    IModuleOptionsProvider options) : ILocalOnlyRuntimeParticipant
{
    private readonly IVtubeStudioIntegration _inner = VtubeStudioModule.GetOrCreateIntegration(services);
    private readonly IModuleOptionsProvider _options = options;

    public string Name => _inner.Name;

    public Task ConfigureAsync(AppConfiguration config, CancellationToken cancellationToken)
    {
        var effective = VtubeStudioModuleBridge.GetEffectiveOptions(_options).ToConnectionOptions();
        return _inner.ConfigureAsync(effective, cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => _inner.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => _inner.StopAsync(cancellationToken);

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
}
