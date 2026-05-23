# Plugin Client Extension Hosts

Issue [#226](https://github.com/honua-io/honua-mobile/issues/226) is scoped to
client host/runtime extension points for mobile and web. Portable plugin
contracts remain outside this repo.

## Ownership Boundary

| Surface | Owner | Notes |
| --- | --- | --- |
| MAUI map plugin activation, host registries, UI mounting, native renderer adapters, and failure isolation | `honua-mobile` | Implemented as thin runtime primitives under `Honua.Mobile.Maui.Plugins`. |
| Web component and browser host extension details | `honua-mobile` / `@honua-io/embed` | Documented here only for this branch; active embed implementation work stays separate. |
| Non-UI plugin manifests, permissions, compatibility checks, validators, calculated fields, data transforms, and workflow hooks | `honua-sdk-dotnet` | SDK issue [honua-sdk-dotnet#72](https://github.com/honua-io/honua-sdk-dotnet/issues/72) is available through versioned `Honua.Sdk.*` packages; mobile consumes those contracts instead of redefining them. |
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
- `HonuaPluginManifest` adapters that preflight SDK-owned manifest validation
  and mobile host compatibility before plugin activation.
- `IHonuaMapPluginTrustService` and `IHonuaMapPluginPermissionService` adapter
  points for host trust checks and permission prompts.
- `AddHonuaMapPluginHost` and `AddHonuaMapPlugin<TPlugin>` DI helpers in a
  separate plugin-host registration extension file.

Use these APIs as host adapter contracts. A mobile plugin package should expose
registration glue, UI surfaces, and native renderer adapters; portable
manifests, permissions, validators, workflow hooks, geometry, and service
clients still come from versioned SDK packages when those contracts exist.

Example:

```csharp
using Honua.Sdk.Abstractions.Plugins;
using Honua.Mobile.Maui.Plugins;
using Microsoft.Extensions.DependencyInjection;

services
    .AddHonuaMapPluginHost()
    .AddHonuaMapPlugin<TenantInspectionMapPlugin>();

public sealed class TenantInspectionMapPlugin : IHonuaMapPlugin
{
    private static readonly HonuaPluginManifest Manifest = HonuaPluginManifest.ParseJson("""
    {
      "schemaVersion": "honua.plugin.v1",
      "pluginId": "tenant.inspections",
      "displayName": "Tenant inspections",
      "publisher": "Tenant",
      "version": "1.0.0",
      "compatibility": {
        "supportedHosts": [ "mobile" ]
      },
      "extensions": [
        {
          "extensionId": "tenant.inspections.validate",
          "type": "field-validator",
          "target": "inspection",
          "handler": "tenant.inspections.validate"
        }
      ]
    }
    """);

    public HonuaMapPluginDescriptor Descriptor { get; } = Manifest.ToMapPluginDescriptor(
        priority: 20);

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
load. Failure messages in activation reports are redacted with the mobile
exception redactor before they are exposed to diagnostics.

This is runtime failure isolation, not a process sandbox. Code signing,
enterprise trust, and package provenance still require the SDK and server work
linked above. The mobile host can fail closed today when its trust service marks
a plugin untrusted or revoked. SDK-owned manifest permissions are passed to the
mobile permission service before activation; required permissions block loading
when not granted, while optional permissions are omitted from the plugin
context.

`HonuaMapPluginDescriptor.SdkManifest` is optional for purely host-local
extensions, but plugin packages that publish portable manifests should attach
the SDK `HonuaPluginManifest` or call `ToMapPluginDescriptor`. The MAUI host
then uses SDK validation and rejects manifests that do not include the `mobile`
host kind before plugin code is activated.

Runtime unload is represented by dropping a plugin contribution snapshot or by
reactivating with `HonuaMapPluginActivationOptions.DisabledPluginIds`. Hosts
that mount toolbar buttons or panels should re-render from the new snapshot.

### Mobile Adapter Checklist

Keep mobile plugin slices mergeable by checking these boundaries before adding a
new extension package:

| Adapter concern | Mobile-owned shape |
| --- | --- |
| Registration | `IServiceCollection` extension methods that call `AddHonuaMapPluginHost` and register plugin implementations. |
| UI mounting | `HonuaMapPluginUiExtension` entries for host-owned panels, dialogs, forms, or workflow screens. |
| Toolbar commands | `HonuaMapPluginToolbarButton` entries that resolve mobile services from `HonuaMapPluginCommandContext.Services`. |
| Feature rendering | `HonuaMapPluginFeatureRenderer` entries that point at native renderer adapter types. |
| SDK dependency | Reference a published `Honua.Sdk.*` package and link the SDK issue when portable contracts are required. |
| Server dependency | Link the `honua-server` dependency issue when plugin behavior requires server endpoints or hooks. |

## Web Host Boundary

Web hosts should expose browser/runtime extension points only:

- ES module registration for map and scene host extensions.
- React/Vue/Svelte component mounting adapters owned by the web host.
- DOM events, CSS custom properties, toolbar controls, panels, and floating
  widgets.
- Failure events that identify which extension failed without tearing down the
  host component.

`@honua-io/embed` registration options now support runtime trust state,
required permission gates, and granted permission checks for browser host
extensions. These are not durable plugin manifests; they are host-side loading
guards. Command click failures are emitted through
`honua-embed-extension-error` with a redacted message and do not remove the
control or break the map/scene element.

Web host plugins should consume SDK-owned TypeScript or generated contract
packages when they need shared manifests, validation, feature queries, routing,
scenes, or data transforms. Until a shared TypeScript package is published,
`@honua-io/embed` should keep extension metadata runtime-only and avoid adding
local manifest schemas.

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
