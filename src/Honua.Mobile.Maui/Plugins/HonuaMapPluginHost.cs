using Microsoft.Extensions.Logging;

namespace Honua.Mobile.Maui.Plugins;

/// <summary>
/// Activation failure captured for one map plugin without stopping the host.
/// </summary>
public sealed record HonuaMapPluginActivationFailure(
    string PluginId,
    string Message,
    Exception Exception);

/// <summary>
/// Result of activating all registered map plugins.
/// </summary>
public sealed record HonuaMapPluginActivationReport
{
    public IReadOnlyList<HonuaMapPluginDescriptor> ActivatedPlugins { get; init; } = [];

    public IReadOnlyList<HonuaMapPluginActivationFailure> Failures { get; init; } = [];

    public HonuaMapPluginContributionSnapshot Contributions { get; init; } = new();

    public bool HasFailures => Failures.Count > 0;
}

/// <summary>
/// Activates MAUI map plugins and isolates failures to the plugin that caused them.
/// </summary>
public sealed class HonuaMapPluginHost
{
    private readonly IReadOnlyList<IHonuaMapPlugin> _plugins;
    private readonly IServiceProvider _services;
    private readonly ILogger<HonuaMapPluginHost>? _logger;

    public HonuaMapPluginHost(
        IEnumerable<IHonuaMapPlugin> plugins,
        IServiceProvider services,
        ILogger<HonuaMapPluginHost>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        _plugins = plugins.ToArray();
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger;
    }

    public async ValueTask<HonuaMapPluginActivationReport> ActivateAsync(CancellationToken ct = default)
    {
        var activated = new List<HonuaMapPluginDescriptor>();
        var failures = new List<HonuaMapPluginActivationFailure>();
        var contributions = new HonuaMapPluginContributionRegistry();
        var candidates = new List<(HonuaMapPluginDescriptor Descriptor, IHonuaMapPlugin Plugin)>();
        var pluginIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var plugin in _plugins)
        {
            ct.ThrowIfCancellationRequested();

            if (plugin is null)
            {
                var ex = new InvalidOperationException("Map plugin registration resolved to null.");
                const string pluginId = "<null>";
                _logger?.LogError(ex, "Map plugin {PluginId} failed during activation.", pluginId);
                failures.Add(new HonuaMapPluginActivationFailure(pluginId, ex.Message, ex));
                continue;
            }

            try
            {
                var descriptor = plugin.Descriptor;
                ArgumentNullException.ThrowIfNull(descriptor);
                descriptor.Validate();
                candidates.Add((descriptor, plugin));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var pluginId = plugin.GetType().FullName ?? plugin.GetType().Name;
                _logger?.LogError(ex, "Map plugin {PluginId} failed during activation.", pluginId);
                failures.Add(new HonuaMapPluginActivationFailure(pluginId, ex.Message, ex));
            }
        }

        foreach (var (descriptor, plugin) in candidates.OrderBy(item => item.Descriptor.Priority))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (!pluginIds.Add(descriptor.Id))
                {
                    throw new InvalidOperationException(
                        $"Map plugin '{descriptor.Id}' is already registered.");
                }

                var pluginContributions = new HonuaMapPluginContributionRegistry();
                var context = new HonuaMapPluginContext(_services, descriptor, pluginContributions);
                await plugin.ActivateAsync(context, ct).ConfigureAwait(false);

                contributions.Merge(pluginContributions);
                activated.Add(descriptor);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Map plugin {PluginId} failed during activation.", descriptor.Id);
                failures.Add(new HonuaMapPluginActivationFailure(descriptor.Id, ex.Message, ex));
            }
        }

        return new HonuaMapPluginActivationReport
        {
            ActivatedPlugins = activated.ToArray(),
            Failures = failures.ToArray(),
            Contributions = contributions.Snapshot(),
        };
    }

    private sealed class HonuaMapPluginContext : IHonuaMapPluginContext
    {
        private readonly HonuaMapPluginContributionRegistry _registry;

        public HonuaMapPluginContext(
            IServiceProvider services,
            HonuaMapPluginDescriptor plugin,
            HonuaMapPluginContributionRegistry registry)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public IServiceProvider Services { get; }

        public HonuaMapPluginDescriptor Plugin { get; }

        public void AddToolbarButton(HonuaMapPluginToolbarButton button)
            => _registry.AddToolbarButton(Plugin.Id, button);

        public void AddUiExtension(HonuaMapPluginUiExtension extension)
            => _registry.AddUiExtension(Plugin.Id, extension);

        public void AddFeatureRenderer(HonuaMapPluginFeatureRenderer renderer)
            => _registry.AddFeatureRenderer(Plugin.Id, renderer);
    }
}
