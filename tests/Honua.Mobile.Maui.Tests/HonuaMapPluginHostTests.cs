using Honua.Mobile.Maui.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Mobile.Maui.Tests;

public sealed class HonuaMapPluginHostTests
{
    [Fact]
    public async Task ActivateAsync_ActivatesHealthyPluginsInPriorityOrder()
    {
        using var provider = new ServiceCollection()
            .AddHonuaMapPlugin(new RecordingMapPlugin("later", priority: 20))
            .AddHonuaMapPlugin(new RecordingMapPlugin("first", priority: 5))
            .BuildServiceProvider();

        var report = await provider.GetRequiredService<HonuaMapPluginHost>().ActivateAsync();

        Assert.False(report.HasFailures);
        Assert.Equal(["first", "later"], report.ActivatedPlugins.Select(plugin => plugin.Id));
        Assert.Equal(["first-action", "later-action"], report.Contributions.ToolbarButtons
            .Select(item => item.Contribution.Id));
        Assert.Equal(["first-panel", "later-panel"], report.Contributions.UiExtensions
            .Select(item => item.Contribution.Id));
        Assert.Equal(["first-renderer", "later-renderer"], report.Contributions.FeatureRenderers
            .Select(item => item.Contribution.Id));
    }

    [Fact]
    public async Task ActivateAsync_DropsContributionsFromPluginThatThrows()
    {
        using var provider = new ServiceCollection()
            .AddHonuaMapPlugin(new RecordingMapPlugin("healthy"))
            .AddHonuaMapPlugin(new ThrowingMapPlugin(addContributionBeforeFailure: true))
            .BuildServiceProvider();

        var report = await provider.GetRequiredService<HonuaMapPluginHost>().ActivateAsync();

        var failure = Assert.Single(report.Failures);
        Assert.Equal("broken", failure.PluginId);
        Assert.IsType<InvalidOperationException>(failure.Exception);
        Assert.Equal("healthy", Assert.Single(report.ActivatedPlugins).Id);
        Assert.DoesNotContain(report.Contributions.ToolbarButtons, item =>
            item.Contribution.Id == "broken-action");
    }

    [Fact]
    public async Task ActivateAsync_TreatsNullPluginRegistrationAsActivationFailure()
    {
        var services = new ServiceCollection()
            .AddHonuaMapPluginHost();
        services.AddSingleton<IHonuaMapPlugin>(_ => null!);
        services.AddSingleton<IHonuaMapPlugin>(new RecordingMapPlugin("healthy"));
        using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HonuaMapPluginHost>().ActivateAsync();

        var failure = Assert.Single(report.Failures);
        Assert.Equal("<null>", failure.PluginId);
        Assert.Equal("Map plugin registration resolved to null.", failure.Message);
        Assert.IsType<InvalidOperationException>(failure.Exception);
        Assert.Equal("healthy", Assert.Single(report.ActivatedPlugins).Id);
    }

    [Fact]
    public async Task ActivateAsync_TreatsDuplicateContributionAsPluginFailure()
    {
        using var provider = new ServiceCollection()
            .AddHonuaMapPlugin(new ToolbarOnlyPlugin("first", "shared-action", priority: 1))
            .AddHonuaMapPlugin(new ToolbarOnlyPlugin("second", "shared-action", priority: 2))
            .BuildServiceProvider();

        var report = await provider.GetRequiredService<HonuaMapPluginHost>().ActivateAsync();

        Assert.Equal("first", Assert.Single(report.ActivatedPlugins).Id);
        Assert.Equal("second", Assert.Single(report.Failures).PluginId);

        var button = Assert.Single(report.Contributions.ToolbarButtons);
        Assert.Equal("first", button.PluginId);
        Assert.Equal("shared-action", button.Contribution.Id);
    }

    [Fact]
    public async Task AddHonuaMapPlugin_RegistersPluginTypeAndHost()
    {
        using var provider = new ServiceCollection()
            .AddHonuaMapPlugin<TypedMapPlugin>()
            .BuildServiceProvider();

        var host = provider.GetRequiredService<HonuaMapPluginHost>();
        var report = await host.ActivateAsync();

        Assert.Equal("typed", Assert.Single(report.ActivatedPlugins).Id);
        Assert.Equal("typed-action", Assert.Single(report.Contributions.ToolbarButtons).Contribution.Id);
    }

    private sealed class RecordingMapPlugin : IHonuaMapPlugin
    {
        private readonly string _id;

        public RecordingMapPlugin(string id, int priority = 0)
        {
            _id = id;
            Descriptor = new HonuaMapPluginDescriptor
            {
                Id = id,
                DisplayName = id,
                Priority = priority,
            };
        }

        public HonuaMapPluginDescriptor Descriptor { get; }

        public ValueTask ActivateAsync(IHonuaMapPluginContext context, CancellationToken ct = default)
        {
            context.AddToolbarButton(CreateToolbarButton($"{_id}-action"));
            context.AddUiExtension(new HonuaMapPluginUiExtension
            {
                Id = $"{_id}-panel",
                Title = $"{_id} panel",
                Kind = HonuaMapPluginUiExtensionKind.Panel,
                ViewModelType = typeof(RecordingMapPlugin),
            });
            context.AddFeatureRenderer(new HonuaMapPluginFeatureRenderer
            {
                Id = $"{_id}-renderer",
                RendererType = typeof(RecordingRenderer),
            });
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ToolbarOnlyPlugin : IHonuaMapPlugin
    {
        private readonly string _buttonId;

        public ToolbarOnlyPlugin(string id, string buttonId, int priority)
        {
            _buttonId = buttonId;
            Descriptor = new HonuaMapPluginDescriptor
            {
                Id = id,
                DisplayName = id,
                Priority = priority,
            };
        }

        public HonuaMapPluginDescriptor Descriptor { get; }

        public ValueTask ActivateAsync(IHonuaMapPluginContext context, CancellationToken ct = default)
        {
            context.AddToolbarButton(CreateToolbarButton(_buttonId));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingMapPlugin : IHonuaMapPlugin
    {
        private readonly bool _addContributionBeforeFailure;

        public ThrowingMapPlugin(bool addContributionBeforeFailure = false)
            => _addContributionBeforeFailure = addContributionBeforeFailure;

        public HonuaMapPluginDescriptor Descriptor { get; } = new()
        {
            Id = "broken",
            DisplayName = "Broken",
            Priority = 10,
        };

        public ValueTask ActivateAsync(IHonuaMapPluginContext context, CancellationToken ct = default)
        {
            if (_addContributionBeforeFailure)
            {
                context.AddToolbarButton(CreateToolbarButton("broken-action"));
            }

            throw new InvalidOperationException("Plugin activation failed.");
        }
    }

    private sealed class TypedMapPlugin : IHonuaMapPlugin
    {
        public HonuaMapPluginDescriptor Descriptor { get; } = new()
        {
            Id = "typed",
            DisplayName = "Typed",
        };

        public ValueTask ActivateAsync(IHonuaMapPluginContext context, CancellationToken ct = default)
        {
            context.AddToolbarButton(CreateToolbarButton("typed-action"));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingRenderer;

    private static HonuaMapPluginToolbarButton CreateToolbarButton(string id)
        => new()
        {
            Id = id,
            Title = id,
            ExecuteAsync = (_, _) => ValueTask.CompletedTask,
        };
}
