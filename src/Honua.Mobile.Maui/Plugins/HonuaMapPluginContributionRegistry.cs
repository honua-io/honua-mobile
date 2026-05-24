namespace Honua.Mobile.Maui.Plugins;

/// <summary>
/// Plugin-owned contribution plus the plugin ID that registered it.
/// </summary>
/// <typeparam name="TContribution">Contribution descriptor type.</typeparam>
public sealed record HonuaMapPluginContribution<TContribution>(
    string PluginId,
    TContribution Contribution);

/// <summary>
/// Immutable view of active map plugin contributions.
/// </summary>
public sealed record HonuaMapPluginContributionSnapshot
{
    public IReadOnlyList<HonuaMapPluginContribution<HonuaMapPluginToolbarButton>> ToolbarButtons { get; init; } =
        [];

    public IReadOnlyList<HonuaMapPluginContribution<HonuaMapPluginUiExtension>> UiExtensions { get; init; } =
        [];

    public IReadOnlyList<HonuaMapPluginContribution<HonuaMapPluginFeatureRenderer>> FeatureRenderers { get; init; } =
        [];

    public HonuaMapPluginContributionSnapshot WithoutPlugin(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        return new HonuaMapPluginContributionSnapshot
        {
            ToolbarButtons = ToolbarButtons
                .Where(item => !string.Equals(item.PluginId, pluginId, StringComparison.Ordinal))
                .ToArray(),
            UiExtensions = UiExtensions
                .Where(item => !string.Equals(item.PluginId, pluginId, StringComparison.Ordinal))
                .ToArray(),
            FeatureRenderers = FeatureRenderers
                .Where(item => !string.Equals(item.PluginId, pluginId, StringComparison.Ordinal))
                .ToArray(),
        };
    }
}

/// <summary>
/// In-memory registry for map plugin host contributions.
/// </summary>
public sealed class HonuaMapPluginContributionRegistry
{
    private readonly Dictionary<string, HonuaMapPluginContribution<HonuaMapPluginToolbarButton>> _toolbarButtons =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, HonuaMapPluginContribution<HonuaMapPluginUiExtension>> _uiExtensions =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, HonuaMapPluginContribution<HonuaMapPluginFeatureRenderer>> _featureRenderers =
        new(StringComparer.Ordinal);

    public void AddToolbarButton(string pluginId, HonuaMapPluginToolbarButton button)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(button);

        button.Validate();
        Add(_toolbarButtons, pluginId, button.Id, button);
    }

    public void AddUiExtension(string pluginId, HonuaMapPluginUiExtension extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(extension);

        extension.Validate();
        Add(_uiExtensions, pluginId, extension.Id, extension);
    }

    public void AddFeatureRenderer(string pluginId, HonuaMapPluginFeatureRenderer renderer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(renderer);

        renderer.Validate();
        Add(_featureRenderers, pluginId, renderer.Id, renderer);
    }

    public HonuaMapPluginContributionSnapshot Snapshot()
        => new()
        {
            ToolbarButtons = _toolbarButtons.Values
                .OrderBy(item => item.Contribution.Order)
                .ThenBy(item => item.Contribution.Id, StringComparer.Ordinal)
                .ToArray(),
            UiExtensions = _uiExtensions.Values
                .OrderBy(item => item.Contribution.Order)
                .ThenBy(item => item.Contribution.Id, StringComparer.Ordinal)
                .ToArray(),
            FeatureRenderers = _featureRenderers.Values
                .OrderBy(item => item.Contribution.Order)
                .ThenBy(item => item.Contribution.Id, StringComparer.Ordinal)
                .ToArray(),
        };

    internal void Merge(HonuaMapPluginContributionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var snapshot = registry.Snapshot();
        foreach (var item in snapshot.ToolbarButtons)
        {
            AddToolbarButton(item.PluginId, item.Contribution);
        }

        foreach (var item in snapshot.UiExtensions)
        {
            AddUiExtension(item.PluginId, item.Contribution);
        }

        foreach (var item in snapshot.FeatureRenderers)
        {
            AddFeatureRenderer(item.PluginId, item.Contribution);
        }
    }

    private static void Add<TContribution>(
        IDictionary<string, HonuaMapPluginContribution<TContribution>> contributions,
        string pluginId,
        string contributionId,
        TContribution contribution)
    {
        if (!contributions.TryAdd(
            contributionId,
            new HonuaMapPluginContribution<TContribution>(pluginId, contribution)))
        {
            throw new InvalidOperationException(
                $"Map plugin contribution '{contributionId}' is already registered.");
        }
    }
}
