# Plugin and Host Extension Boundary

Honua mobile and embed hosts can expose extension points for runtime behavior,
but shared plugin contracts and manifests belong in `honua-sdk-dotnet` packages.
This repo should stay focused on host wiring, UI integration, renderer adapters,
storage, permissions, and lifecycle concerns.

## Ownership

| Surface | Owner | Notes |
| --- | --- | --- |
| Plugin manifest schema, source descriptors, shared DTOs, validation rules | `honua-sdk-dotnet` | Publish as versioned `Honua.Sdk.*` packages and consume them here. |
| Web component controls, DOM events, white-label themes, snippet generation | `honua-mobile` / `@honua-io/embed` | Runtime integration for browser and WebView hosts. |
| MAUI dependency injection, platform permissions, storage, camera, GPS, sensors | `honua-mobile` | Plugin packages should provide mobile registration glue instead of new neutral clients. |
| Server capabilities needed by plugins | `honua-server` | Link server dependency issues from the mobile issue. |

## Web Host Extensions

`@honua-io/embed` exposes a runtime registry for lightweight host extensions:

```ts
import { registerHonuaEmbedExtension } from '@honua-io/embed';

const registration = registerHonuaEmbedExtension({
  id: 'tenant-tools',
  target: 'map',
  priority: 10,
  activate(context) {
    const control = context.addControl({
      id: 'tenant-action',
      label: 'Open tenant action',
      text: 'T',
      onClick: (_event, clickContext) => {
        clickContext.dispatch('tenant-action', {
          config: clickContext.config,
        });
      },
    });

    context.setCssVariable('--honua-map-accent', '#0f766e');
    return () => control.remove();
  },
});
```

Extension lifecycle:

| Hook | When it runs |
| --- | --- |
| `activate(context)` | When a matching `<honua-map>` or `<honua-scene>` connects, or when the extension is registered after elements already exist. |
| `configChanged(context)` | After a connected element emits its config-change event. |
| `registration.unregister()` | Removes mounted extension controls from active elements and runs cleanup callbacks. |

The context intentionally exposes host runtime capabilities only: the target
element, open shadow root, current config, control mounting, CSS variables, and
composed DOM event dispatch. It does not define plugin manifests, data schemas,
source contracts, auth contracts, or feature query/edit APIs.

## MAUI Host Extensions

Mobile extension packages should follow the existing `IServiceCollection`
pattern in `Honua.Mobile.Maui`:

```csharp
public static IServiceCollection AddTenantFieldTools(
    this IServiceCollection services,
    TenantFieldToolOptions options)
{
    ArgumentNullException.ThrowIfNull(services);
    ArgumentNullException.ThrowIfNull(options);

    services.AddSingleton(options);
    services.AddSingleton<TenantFieldToolViewModel>();
    return services;
}
```

Registration code may compose existing mobile-owned services such as map
annotations, offline storage adapters, camera workflows, and background sync.
When an extension needs portable contracts, feature clients, routing, scenes,
field schemas, validation, or plugin manifests, add or consume versioned
`Honua.Sdk.*` packages rather than adding platform-neutral models here.

## Embed Snippet Generation

Use `createHonuaMapSnippet` for white-label map embeds and tenant-specific tag
names:

```ts
import { createHonuaMapSnippet } from '@honua-io/embed';

const snippet = createHonuaMapSnippet({
  serviceUrl: 'https://services.honua.example/FeatureServer',
  layerIds: ['assets'],
  label: 'Tenant asset map',
  style: {
    accent: '#334155',
    fontFamily: 'Aptos, sans-serif',
  },
}, {
  elementName: 'tenant-asset-map',
});
```

Generated snippets omit `apiKey` unless `includeCredentials: true` is supplied.
Only browser-safe public credentials should be emitted into tenant-facing markup.
