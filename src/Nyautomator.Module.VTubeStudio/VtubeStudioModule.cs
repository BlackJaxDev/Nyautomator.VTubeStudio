using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nyautomator.Module.Abstractions;
using Nyautomator.Runtime.Abstractions;
using Nyautomator.Runtime.Integrations.VTubeStudio;

namespace Nyautomator.Modules.VTubeStudio;

public sealed class VtubeStudioModule : INyautomatorModule
{
    private static readonly object FallbackGate = new();
    private static IVtubeStudioIntegration? _fallbackIntegration;
    private static VtubeStudioModuleBridge? _fallbackBridge;

    public NyautomatorModuleDescriptor Descriptor { get; } = new(
        "vtubestudio",
        "VTube Studio",
        "VTube Studio Public API, auth, runtime events, and automation nodes.",
        "0.1.0");

    public void ConfigureServices(NyautomatorModuleServiceContext context, IServiceCollection services)
    {
        services.AddNyautomatorVtubeStudioIntegration();
        services.AddSingleton<VtubeStudioModuleBridge>();
    }

    public void ConfigureRuntime(NyautomatorModuleRuntimeContext context)
    {
        var defaults = JsonSerializer.SerializeToElement(
            VtubeStudioModuleOptions.CreateDefault(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var configuration = new ModuleConfigurationContribution(
            "options",
            Defaults: defaults,
            ModuleId: context.ModuleId);

        context.Configurations.Register(configuration);
        context.Contributions.Register(new ModuleContributionSet(context.ModuleId, Configurations: [configuration]));

        var bridge = context.Services.GetService<VtubeStudioModuleBridge>() ?? GetFallbackBridge(context.Services);
        bridge.WireAutomationHost();

        context.ApiHandlers.Register(bridge);
        context.AuthenticatedIntegrations.Register(bridge);
        context.AuthenticatedIntegrations.Register(new AuthenticatedIntegrationAliasAdapter("vts", bridge));
    }

    internal static IVtubeStudioIntegration GetOrCreateIntegration(IServiceProvider services)
    {
        lock (FallbackGate)
        {
            _fallbackIntegration ??= services.GetService<IVtubeStudioIntegration>() ?? new VtubeStudioIntegration();
            return _fallbackIntegration;
        }
    }

    private static VtubeStudioModuleBridge GetFallbackBridge(IServiceProvider services)
    {
        lock (FallbackGate)
        {
            _fallbackBridge ??= new VtubeStudioModuleBridge(
                services.GetRequiredService<IModuleOptionsProvider>(),
                GetOrCreateIntegration(services));
            return _fallbackBridge;
        }
    }

    private sealed class AuthenticatedIntegrationAliasAdapter(string id, IAuthenticatedIntegrationAdapter inner)
        : IAuthenticatedIntegrationAdapter
    {
        public string Id { get; } = id;

        public Task<AuthenticatedIntegrationResponse> SendAsync(
            AuthenticatedIntegrationRequest request,
            CancellationToken cancellationToken)
            => inner.SendAsync(request, cancellationToken);
    }
}
