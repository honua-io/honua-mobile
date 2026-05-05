using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Honua.Mobile.Maui.Plugins;

/// <summary>
/// Dependency injection helpers for MAUI map plugin hosts.
/// </summary>
public static class HonuaMapPluginServiceCollectionExtensions
{
    public static IServiceCollection AddHonuaMapPluginHost(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(sp => new HonuaMapPluginHost(
            sp.GetServices<IHonuaMapPlugin>(),
            sp,
            sp.GetService<ILogger<HonuaMapPluginHost>>()));
        return services;
    }

    public static IServiceCollection AddHonuaMapPlugin<TPlugin>(this IServiceCollection services)
        where TPlugin : class, IHonuaMapPlugin
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHonuaMapPluginHost();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHonuaMapPlugin, TPlugin>());
        return services;
    }

    public static IServiceCollection AddHonuaMapPlugin(
        this IServiceCollection services,
        IHonuaMapPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(plugin);

        services.AddHonuaMapPluginHost();
        services.AddSingleton<IHonuaMapPlugin>(plugin);
        return services;
    }
}
