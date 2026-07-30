using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lifecycle.Hosting;

public static class LifecycleHostingServiceCollectionExtensions
{
    /// <summary>Runs all registered <see cref="Lifecycle.ILifecycle"/> instances with the Generic Host.</summary>
    public static IServiceCollection AddLifecycleHosting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IHostedService, LifecycleHostedService>();
        return services;
    }
}
