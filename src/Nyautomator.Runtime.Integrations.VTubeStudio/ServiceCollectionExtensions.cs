using Microsoft.Extensions.DependencyInjection;

namespace Nyautomator.Runtime.Integrations.VTubeStudio;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNyautomatorVtubeStudioIntegration(this IServiceCollection services)
    {
        services.AddSingleton<IVtubeStudioIntegration, VtubeStudioIntegration>();
        return services;
    }
}
