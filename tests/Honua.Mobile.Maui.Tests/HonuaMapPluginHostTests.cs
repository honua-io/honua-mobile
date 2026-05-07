using Honua.Mobile.Maui.Plugins;
using Honua.Sdk.Abstractions.Plugins;
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
    public async Task ActivateAsync_TreatsDuplicatePluginIdAsActivationFailure()
    {
        using var provider = new ServiceCollection()
            .AddHonuaMapPlugin(new ToolbarOnlyPlugin("shared", "first-action", priority: 1))
            .AddHonuaMapPlugin(new ToolbarOnlyPlugin("shared", "second-action", priority: 2))
            .AddHonuaMapPlugin(new ToolbarOnlyPlugin("later", "later-action", priority: 3))
            .BuildServiceProvider();

        var report = await provider.GetRequiredService<HonuaMapPluginHost>().ActivateAsync();

        Assert.Equal(["shared", "later"], report.ActivatedPlugins.Select(plugin => plugin.Id));

        var failure = Assert.Single(report.Failures);
        Assert.Equal("shared", failure.PluginId);
        Assert.Contains("already registered", failure.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(failure.Exception);

        Assert.Equal(["first-action", "later-action"], report.Contributions.ToolbarButtons
            .Select(item => item.Contribution.Id));
        Assert.DoesNotContain(report.Contributions.ToolbarButtons, item =>
            item.Contribution.Id == "second-action");
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

    [Fact]
    public void ToMapPluginDescriptor_UsesSdkOwnedManifestMetadata()
    {
        var manifest = CreateSdkManifest(
            "com.example.inspection",
            "Inspection Tools",
            [HonuaPluginHostKinds.Mobile]);

        var descriptor = manifest.ToMapPluginDescriptor(priority: 10);

        Assert.Equal("com.example.inspection", descriptor.Id);
        Assert.Equal("Inspection Tools", descriptor.DisplayName);
        Assert.Equal(new Version(1, 2, 3), descriptor.Version);
        Assert.Equal(10, descriptor.Priority);
        Assert.Same(manifest, descriptor.SdkManifest);
    }

    [Fact]
    public async Task ActivateAsync_TreatsSdkManifestHostMismatchAsActivationFailure()
    {
        using var provider = new ServiceCollection()
            .AddHonuaMapPlugin(new ManifestBackedMapPlugin(CreateSdkManifest(
                "web-only",
                "Web Only",
                [HonuaPluginHostKinds.Web])))
            .AddHonuaMapPlugin(new RecordingMapPlugin("healthy"))
            .BuildServiceProvider();

        var report = await provider.GetRequiredService<HonuaMapPluginHost>().ActivateAsync();

        var failure = Assert.Single(report.Failures);
        Assert.Equal(typeof(ManifestBackedMapPlugin).FullName, failure.PluginId);
        Assert.Contains("mobile", failure.Message, StringComparison.Ordinal);
        Assert.Equal("healthy", Assert.Single(report.ActivatedPlugins).Id);
    }

    [Fact]
    public void EvaluateForMobileHost_ReturnsSdkValidationErrors()
    {
        var manifest = CreateSdkManifest(
            string.Empty,
            "Missing Id",
            [HonuaPluginHostKinds.Mobile]);

        var evaluation = manifest.EvaluateForMobileHost();

        Assert.False(evaluation.CanLoad);
        Assert.False(evaluation.SdkValidation.IsValid);
        Assert.Contains(evaluation.SdkValidation.Issues, issue =>
            issue.Severity == HonuaPluginValidationSeverity.Error);
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

    private sealed class ManifestBackedMapPlugin : IHonuaMapPlugin
    {
        public ManifestBackedMapPlugin(HonuaPluginManifest manifest)
        {
            Descriptor = new HonuaMapPluginDescriptor
            {
                Id = manifest.PluginId ?? string.Empty,
                DisplayName = manifest.DisplayName ?? string.Empty,
                SdkManifest = manifest,
            };
        }

        public HonuaMapPluginDescriptor Descriptor { get; }

        public ValueTask ActivateAsync(IHonuaMapPluginContext context, CancellationToken ct = default)
        {
            context.AddToolbarButton(CreateToolbarButton($"{context.Plugin.Id}-action"));
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

    private static HonuaPluginManifest CreateSdkManifest(
        string pluginId,
        string displayName,
        IReadOnlyList<string> hosts)
        => new()
        {
            SchemaVersion = HonuaPluginManifest.CurrentSchemaVersion,
            PluginId = pluginId,
            DisplayName = displayName,
            Publisher = "Example",
            Version = "1.2.3",
            Compatibility = new HonuaPluginCompatibility
            {
                SupportedHosts = hosts,
            },
            Extensions =
            [
                new HonuaPluginExtensionPoint
                {
                    ExtensionId = "field-validation",
                    Type = HonuaPluginExtensionTypes.FieldValidator,
                    Target = "field",
                    Handler = "example.field-validation",
                    Order = 0,
                },
            ],
        };
}
