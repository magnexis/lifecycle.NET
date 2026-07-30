using Lifecycle;

namespace Microsoft.Extensions.DependencyInjection;

public static class LifecycleServiceCollectionExtensions
{
    public static IServiceCollection AddLifecycle(this IServiceCollection services) { ArgumentNullException.ThrowIfNull(services); services.AddSingleton(new LifecycleOptions()); return services; }
    public static IServiceCollection AddLifecycle<TLifecycle>(this IServiceCollection services) where TLifecycle : class, ILifecycle { ArgumentNullException.ThrowIfNull(services); services.AddSingleton<TLifecycle>(); services.AddSingleton<ILifecycle>(provider => provider.GetRequiredService<TLifecycle>()); return services; }
}
