// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using Honua.Mobile.Maui.Diagnostics;
using Honua.Sdk.Abstractions.Plugins;
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

    public HonuaMapPluginActivationReport WithoutPlugin(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        return this with
        {
            ActivatedPlugins = ActivatedPlugins
                .Where(plugin => !string.Equals(plugin.Id, pluginId, StringComparison.Ordinal))
                .ToArray(),
            Contributions = Contributions.WithoutPlugin(pluginId),
        };
    }
}

/// <summary>
/// Activates MAUI map plugins and isolates failures to the plugin that caused them.
/// </summary>
public sealed class HonuaMapPluginHost
{
    private static readonly MobileExceptionReportingOptions FailureRedactionOptions = new()
    {
        MaxMessageLength = 500,
    };

    private readonly IReadOnlyList<IHonuaMapPlugin> _plugins;
    private readonly IServiceProvider _services;
    private readonly IHonuaMapPluginTrustService _trustService;
    private readonly IHonuaMapPluginPermissionService _permissionService;
    private readonly ILogger<HonuaMapPluginHost>? _logger;

    public HonuaMapPluginHost(
        IEnumerable<IHonuaMapPlugin> plugins,
        IServiceProvider services,
        ILogger<HonuaMapPluginHost>? logger = null)
        : this(
            plugins,
            services,
            new LocalHonuaMapPluginTrustService(),
            new DenyByDefaultHonuaMapPluginPermissionService(),
            logger)
    {
    }

    public HonuaMapPluginHost(
        IEnumerable<IHonuaMapPlugin> plugins,
        IServiceProvider services,
        IHonuaMapPluginTrustService trustService,
        IHonuaMapPluginPermissionService permissionService,
        ILogger<HonuaMapPluginHost>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        _plugins = plugins.ToArray();
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _trustService = trustService ?? throw new ArgumentNullException(nameof(trustService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _logger = logger;
    }

    public async ValueTask<HonuaMapPluginActivationReport> ActivateAsync(CancellationToken ct = default)
        => await ActivateAsync(new HonuaMapPluginActivationOptions(), ct).ConfigureAwait(false);

    public async ValueTask<HonuaMapPluginActivationReport> ActivateAsync(
        HonuaMapPluginActivationOptions? options,
        CancellationToken ct = default)
    {
        options ??= new HonuaMapPluginActivationOptions();
        var activated = new List<HonuaMapPluginDescriptor>();
        var failures = new List<HonuaMapPluginActivationFailure>();
        var contributions = new HonuaMapPluginContributionRegistry();
        var candidates = new List<(
            HonuaMapPluginDescriptor Descriptor,
            IHonuaMapPlugin Plugin,
            IReadOnlyList<HonuaPluginPermissionDeclaration> GrantedPermissions)>();
        var pluginIds = new HashSet<string>(StringComparer.Ordinal);
        var disabledPluginIds = options.DisabledPluginIds.ToHashSet(StringComparer.Ordinal);

        foreach (var plugin in _plugins)
        {
            ct.ThrowIfCancellationRequested();

            if (plugin is null)
            {
                var ex = new InvalidOperationException("Map plugin registration resolved to null.");
                const string pluginId = "<null>";
                AddFailure(failures, pluginId, ex);
                continue;
            }

            try
            {
                var descriptor = plugin.Descriptor;
                ArgumentNullException.ThrowIfNull(descriptor);
                descriptor.Validate();

                if (disabledPluginIds.Contains(descriptor.Id))
                {
                    continue;
                }

                IReadOnlyList<HonuaPluginPermissionDeclaration> grantedPermissions;
                try
                {
                    await EnsureTrustedAsync(descriptor, ct).ConfigureAwait(false);
                    grantedPermissions = await RequestRequiredPermissionsAsync(descriptor, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AddFailure(failures, descriptor.Id, ex);
                    continue;
                }

                candidates.Add((descriptor, plugin, grantedPermissions));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var pluginId = plugin.GetType().FullName ?? plugin.GetType().Name;
                AddFailure(failures, pluginId, ex);
            }
        }

        foreach (var (descriptor, plugin, grantedPermissions) in candidates.OrderBy(item => item.Descriptor.Priority))
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
                var context = new HonuaMapPluginContext(
                    _services,
                    descriptor,
                    grantedPermissions,
                    pluginContributions);
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
                AddFailure(failures, descriptor.Id, ex);
            }
        }

        return new HonuaMapPluginActivationReport
        {
            ActivatedPlugins = activated.ToArray(),
            Failures = failures.ToArray(),
            Contributions = contributions.Snapshot(),
        };
    }

    private async ValueTask EnsureTrustedAsync(
        HonuaMapPluginDescriptor descriptor,
        CancellationToken ct)
    {
        var trust = await _trustService.EvaluateTrustAsync(descriptor, ct).ConfigureAwait(false);
        if (!trust.CanLoad)
        {
            var reason = string.IsNullOrWhiteSpace(trust.Reason)
                ? "plugin trust state is not approved"
                : trust.Reason;
            throw new InvalidOperationException(
                $"Map plugin '{descriptor.Id}' cannot load because its trust state is {trust.State}: {reason}");
        }
    }

    private async ValueTask<IReadOnlyList<HonuaPluginPermissionDeclaration>> RequestRequiredPermissionsAsync(
        HonuaMapPluginDescriptor descriptor,
        CancellationToken ct)
    {
        var requested = descriptor.SdkManifest?.Permissions ?? [];
        if (requested.Count == 0)
        {
            return [];
        }

        var granted = new List<HonuaPluginPermissionDeclaration>();
        foreach (var permission in requested)
        {
            ct.ThrowIfCancellationRequested();
            var decision = await _permissionService.RequestPermissionAsync(
                new HonuaMapPluginPermissionRequest
                {
                    Plugin = descriptor,
                    Permission = permission,
                },
                ct).ConfigureAwait(false);

            if (decision.Granted)
            {
                granted.Add(permission);
                continue;
            }

            if (permission.Required)
            {
                var reason = string.IsNullOrWhiteSpace(decision.Reason)
                    ? "permission was denied"
                    : decision.Reason;
                throw new InvalidOperationException(
                    $"Map plugin '{descriptor.Id}' required permission '{FormatPermission(permission)}' was denied: {reason}");
            }
        }

        return granted.ToArray();
    }

    private static string FormatPermission(HonuaPluginPermissionDeclaration permission)
        => string.IsNullOrWhiteSpace(permission.Access)
            ? permission.Permission ?? string.Empty
            : $"{permission.Permission}:{permission.Access}";

    private void AddFailure(
        ICollection<HonuaMapPluginActivationFailure> failures,
        string pluginId,
        Exception exception)
    {
        var safePluginId = MobileExceptionRedactor.RedactText(
            pluginId,
            FailureRedactionOptions,
            FailureRedactionOptions.MaxMessageLength) ?? "<unknown>";
        var safeMessage = MobileExceptionRedactor.RedactText(
            exception.Message,
            FailureRedactionOptions,
            FailureRedactionOptions.MaxMessageLength) ?? "Map plugin activation failed.";

        _logger?.LogError(
            "Map plugin {PluginId} failed during activation: {Message}",
            safePluginId,
            safeMessage);
        failures.Add(new HonuaMapPluginActivationFailure(safePluginId, safeMessage, exception));
    }

    private sealed class HonuaMapPluginContext : IHonuaMapPluginContext
    {
        private readonly HonuaMapPluginContributionRegistry _registry;

        public HonuaMapPluginContext(
            IServiceProvider services,
            HonuaMapPluginDescriptor plugin,
            IReadOnlyList<HonuaPluginPermissionDeclaration> grantedPermissions,
            HonuaMapPluginContributionRegistry registry)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            GrantedPermissions = grantedPermissions ?? throw new ArgumentNullException(nameof(grantedPermissions));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public IServiceProvider Services { get; }

        public HonuaMapPluginDescriptor Plugin { get; }

        public IReadOnlyList<HonuaPluginPermissionDeclaration> GrantedPermissions { get; }

        public bool HasPermission(string permission, string access)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(permission);
            ArgumentException.ThrowIfNullOrWhiteSpace(access);

            return GrantedPermissions.Any(grant =>
                string.Equals(grant.Permission, permission, StringComparison.Ordinal) &&
                string.Equals(grant.Access, access, StringComparison.Ordinal));
        }

        public void AddToolbarButton(HonuaMapPluginToolbarButton button)
            => _registry.AddToolbarButton(Plugin.Id, button);

        public void AddUiExtension(HonuaMapPluginUiExtension extension)
            => _registry.AddUiExtension(Plugin.Id, extension);

        public void AddFeatureRenderer(HonuaMapPluginFeatureRenderer renderer)
            => _registry.AddFeatureRenderer(Plugin.Id, renderer);
    }
}
