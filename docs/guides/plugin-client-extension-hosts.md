# Plugin Client Extension Hosts

Issue [#16](https://github.com/honua-io/honua-mobile/issues/16) is scoped to
client host/runtime extension points for mobile and web. Portable plugin
contracts remain outside this repo.

## Ownership Boundary

| Surface | Owner | Notes |
| --- | --- | --- |
| MAUI map plugin activation, host registries, UI mounting, native renderer adapters, and failure isolation | `honua-mobile` | Implemented as thin runtime primitives under `Honua.Mobile.Maui.Plugins`. |
| Web component and browser host extension details | `honua-mobile` / `@honua-io/embed` | Documented here only for this branch; active embed implementation work stays separate. |
| Non-UI plugin manifests, permissions, compatibility checks, validators, calculated fields, data transforms, and workflow hooks | `honua-sdk-dotnet` | Track in [honua-sdk-dotnet#72](https://github.com/honua-io/honua-sdk-dotnet/issues/72) and consume as versioned `Honua.Sdk.*` NuGet packages. |
| Server plugin endpoints, server-side hooks, validators, computed fields, marketplace/discovery APIs, and trusted package metadata | `honua-server` | Track in [honua-server#347](https://github.com/honua-io/honua-server/issues/347). |

The mobile repo should not define durable copies of SDK-owned schemas. If a
plugin needs source descriptors, field schema, validation, routing, scene,
feature query/edit, permission, or manifest contracts, add or consume a
published `Honua.Sdk.*` package and keep this repo limited to adapters and host
registration.

## MAUI Host APIs

`Honua.Mobile.Maui.Plugins` provides host-owned extension points:

- `IHonuaMapPlugin` for runtime map plugins.
- `HonuaMapPluginHost` for activating registered plugins.
- `HonuaMapPluginContributionRegistry` snapshots for toolbar buttons, UI
  extensions, and native feature renderer adapters.
- `AddHonuaMapPluginHost` and `AddHonuaMapPlugin<TPlugin>` DI helpers in a
  separate plugin-host registration extension file.

Example:

```csharp
using Honua.Mobile.Maui.Plugins;
using Microsoft.Extensions.DependencyInjection;

services
    .AddHonuaMapPluginHost()
    .AddHonuaMapPlugin<TenantInspectionMapPlugin>();

public sealed class TenantInspectionMapPlugin : IHonuaMapPlugin
{
    public HonuaMapPluginDescriptor Descriptor { get; } = new()
    {
        Id = "tenant.inspections",
        DisplayName = "Tenant inspections",
        Version = new Version(1, 0, 0),
        Priority = 20,
    };

    public ValueTask ActivateAsync(IHonuaMapPluginContext context, CancellationToken ct = default)
    {
        context.AddToolbarButton(new HonuaMapPluginToolbarButton
        {
            Id = "tenant.inspections.capture",
            Title = "Capture inspection",
            Icon = "camera",
            ExecuteAsync = async (command, token) =>
            {
                var capture = command.Services.GetRequiredService<TenantInspectionCapture>();
                await capture.StartAsync(token).ConfigureAwait(false);
            },
        });

        context.AddUiExtension(new HonuaMapPluginUiExtension
        {
            Id = "tenant.inspections.panel",
            Title = "Inspections",
            Kind = HonuaMapPluginUiExtensionKind.Panel,
            ViewModelType = typeof(TenantInspectionPanelViewModel),
        });

        return ValueTask.CompletedTask;
    }
}
```

`HonuaMapPluginHost.ActivateAsync` isolates failures per plugin. A plugin that
throws while activating, or registers duplicate contributions, is reported in
`HonuaMapPluginActivationReport.Failures`. Contributions from that failed
plugin are not merged into the host snapshot, so other plugins can continue to
load.

This is runtime failure isolation, not a process sandbox. Code signing,
enterprise trust, permission enforcement, and package provenance require the SDK
and server work linked above.

## Web Host Boundary

Web hosts should expose browser/runtime extension points only:

- ES module registration for map and scene host extensions.
- React/Vue/Svelte component mounting adapters owned by the web host.
- DOM events, CSS custom properties, toolbar controls, panels, and floating
  widgets.
- Failure events that identify which extension failed without tearing down the
  host component.

Web host plugins should consume SDK-owned TypeScript or generated contract
packages when they need shared manifests, permissions, validation, feature
queries, routing, scenes, or data transforms. This branch intentionally does not
change `src/Honua.Embed` while embed PRs are active.

## Intentionally Not Implemented Here

- No mobile-local plugin manifest, permission, validator, data-transform, or
  workflow hook contracts.
- No new plain `net*` clients for server plugin APIs.
- No local copies of SDK geometry, source, scene, field, routing, or feature
  edit contracts.
- No React Native bridge, web ES module loader, marketplace, code-signing,
  package discovery, or hot-reload implementation in this branch.
- No long-lived `ProjectReference` to `honua-sdk-dotnet`; consume published
  `Honua.Sdk.*` packages as contracts become available.
