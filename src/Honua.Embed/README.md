# @honua-io/embed

Framework-agnostic web components for embedding Honua map and 3D scene views in SaaS and ISV applications.

## Install

```bash
npm install @honua-io/embed
```

## Map Use

```html
<script type="module">
  import '@honua-io/embed';
</script>

<honua-map
  service-url="https://services.honua.example/FeatureServer"
  layer-ids="assets,work-orders"
  center="21.3069,-157.8583"
  zoom="12"
  basemap="streets"
  interactive
  search
  identify
  attribution="City GIS">
</honua-map>
```

For production display, pair MapLibre GL JS with the deck.gl adapter:

```js
import maplibregl from 'maplibre-gl';
import { HonuaWebDisplayAdapter } from '@honua-io/embed';

const map = new maplibregl.Map({
  container: 'map',
  style: 'https://tiles.example/styles/streets.json',
  center: [-157.8583, 21.3069],
  zoom: 12,
});
const display = new HonuaWebDisplayAdapter(map);

display.setFeatureQueryResult(featureQueryResultPage, {
  source: sourceDescriptor,
});
display.fitToSource(sourceDescriptor, { padding: 32 });
```

The adapter keeps MapLibre responsible for the base map and camera while deck.gl
owns Honua feature overlays. Use `setView(...)` for imperative center/zoom or
bounds changes, `setFeatureQueryResults(...)` for query batches,
`getFeatureCollection(...)` for cached GeoJSON, and `removeLayer(...)` or
`clearFeatureLayers()` when a host screen changes active sources.

## Scene Use

```html
<script type="module">
  import '@honua-io/embed';
</script>

<honua-scene
  tileset-url="https://example.com/tileset.json"
  center="21.3069,-157.8583"
  height="1800"
  heading="20"
  pitch="-35">
</honua-scene>
```

`<honua-scene>` uses CesiumJS from npm and the package build copies Cesium runtime assets into `dist/cesium`. CesiumJS is Apache-2.0 open source; Cesium ion is optional and only needed when an integrator chooses ion-hosted assets or services.

Offline browser/WebView packages can assign a host-controlled resolver:

```js
import { createCacheStorageScenePackageResolver } from '@honua-io/embed';

const scene = document.querySelector('honua-scene');
scene.packageAssetResolver = createCacheStorageScenePackageResolver({
  cacheName: 'honua-scene-packages',
  responseMode: 'cache-url',
});
scene.setAttribute('package-id', manifest.packageId);
scene.setAttribute('tileset-asset', primaryTileset.path);
scene.setAttribute('package-expires-at', manifest.offlineUseExpiresAtUtc);
```

`responseMode: 'cache-url'` returns stable package-local URLs so Cesium can
resolve nested 3D Tiles, terrain, textures, and metadata relative to the entry
asset. Serve the configured URL prefix from a service worker or WebView bridge
with `matchCacheStorageScenePackageRequest(...)`. Use `responseMode:
'object-url'` only for standalone assets or hosts that rewrite nested
references.

## Map Attributes

| Attribute | Purpose |
| --- | --- |
| `service-url` | Honua service or FeatureServer base URL. |
| `layer-ids` | Comma-separated layer identifiers. |
| `api-key` | API key for integrations. It is available in `element.config` but never rendered. |
| `center` | Initial latitude/longitude pair, for example `21.3069,-157.8583`. |
| `zoom` | Initial zoom level, clamped from `0` to `24`. |
| `bbox` | Initial bounds as `minLon,minLat,maxLon,maxLat`. |
| `basemap` | `streets`, `satellite`, `dark`, or an integration-defined style id. |
| `interactive` | Enables keyboard focus and zoom controls. |
| `search` | Shows the search control and emits `honua-map-search`. |
| `identify` | Enables click/identify events and emits `honua-map-identify`. |
| `attribution` | Optional attribution text. No Honua branding is shown by default. |
| `theme` | `light` or `dark`. |
| `label` | Accessible map label, defaulting to `Embedded map`. |

## Scene Attributes

| Attribute | Purpose |
| --- | --- |
| `tileset-url` | External or Honua-hosted 3D Tiles `tileset.json` URL. |
| `terrain-url` | Optional Cesium terrain provider URL. |
| `package-id` | SDK-validated offline scene package identifier. |
| `tileset-asset` | Package-local `tileset.json` path resolved by `packageAssetResolver`. |
| `terrain-asset` | Optional package-local terrain asset path resolved by `packageAssetResolver`. |
| `package-expires-at` | Offline-use expiry timestamp. Expired packages emit `expired-package`. |
| `ion-token` | Optional Cesium ion token. It is not rendered. |
| `cesium-base-url` | Optional URL for hosted Cesium `Assets`, `Workers`, `ThirdParty`, and `Widgets`. |
| `metadata-url` | URL of a `honua-scene-metadata/v1` document. The scene fetches and parses it, then emits `honua-scene-metadata-change`. |
| `center` | Initial latitude/longitude pair, for example `21.3069,-157.8583`. |
| `height` | Initial camera height in meters. |
| `heading` | Initial camera heading in degrees. |
| `pitch` | Initial camera pitch in degrees. |
| `roll` | Initial camera roll in degrees. |
| `autoload` | Set to `false`, `0`, or `no` to disable automatic loading. |
| `theme` | `light` or `dark`. |

`HonuaSceneElement` also exposes an imperative API: `metadata` (property),
`applyView(view)`, `addLayer(metadata)`, `removeLayer(id)`,
`setLayerVisibility(id, visible)`, `setLayerOpacity(id, opacity)`,
`getLayer(id)`, and `samplePoint(x, y)` (returns
`{ latitude, longitude, height } | null` for the surface picked at canvas-space
`x`/`y`). `addLayer` is also re-entrant — calling it again with the same `id`
but a different `kind` or `url` detaches the previous tileset and reloads from
the new source. See the [Scene Controls guide](../../docs/guides/scene-controls.md)
for the metadata shape and the full event reference.

## Events

| Event | Detail |
| --- | --- |
| `honua-map-ready` | Current `HonuaMapConfig`. |
| `honua-map-config-change` | Current `HonuaMapConfig`. |
| `honua-map-search` | `{ query, config }`. |
| `honua-map-identify` | `{ x, y, config }`. |
| `honua-scene-ready` | `{ config, widget, tileset }`. |
| `honua-scene-config-change` | Current `HonuaSceneConfig`. |
| `honua-scene-load-error` | `{ source, code, message, config, error }`. |
| `honua-scene-camera-change` | `{ center, height, orientation, config }`. |
| `honua-scene-identify` | `{ x, y, picked, position, config }` where `position` is `{ latitude, longitude, height } \| null`. |
| `honua-scene-metadata-change` | `{ metadata, source }`. |
| `honua-scene-metadata-error` | `{ url, code, message, path?, error? }`. |
| `honua-scene-layer-change` | `{ layerId, reason, layer, visible, opacity }`. |
| `honua-scene-layer-toggle` | `{ layerId, visible, controlId }` (from `<honua-scene-layers>`). |
| `honua-scene-layer-opacity` | `{ layerId, opacity, controlId }`. |
| `honua-scene-bookmark-apply` | `{ bookmarkId, view, controlId }`. |
| `honua-scene-timeline-change` | `{ phaseId, startUtc, endUtc, visibleLayerIds, controlId }`. |
| `honua-scene-compare-set` | `{ modeId, side, leftLayerIds, rightLayerIds, controlId }`. |
| `honua-scene-feature-select` | `{ featureId, attributes, controlId }`. |
| `honua-scene-measurement-add` | `{ measurementId, kind, points, distance?, area?, controlId }`. |
| `honua-scene-measurement-clear` | `{ measurementId, controlId }`. |
| `honua-scene-control-error` | `{ controlId, kind, message, error? }`. |
| `honua-embed-extension-error` | `{ extensionId, target, lifecycle, error }`. |

## Scene Controls

`<honua-scene-layers>`, `<honua-scene-bookmarks>`, `<honua-scene-timeline>`,
`<honua-scene-compare>`, `<honua-scene-inspector>`, and
`<honua-scene-measure>` compose with `<honua-scene>` and consume a typed
`HonuaSceneMetadata` document instead of hard-coded URLs. Bind a control to a
scene with `for="<css-selector>"` (or place it as a sibling and let it resolve
the nearest preceding `<honua-scene>`).

```html
<honua-scene
  id="scene"
  metadata-url="/scenes/site.json"
  tileset-url="https://example.test/site/primary/tileset.json">
</honua-scene>
<honua-scene-layers for="#scene"></honua-scene-layers>
<honua-scene-bookmarks for="#scene"></honua-scene-bookmarks>
<honua-scene-timeline for="#scene"></honua-scene-timeline>
<honua-scene-compare for="#scene"></honua-scene-compare>
<honua-scene-inspector for="#scene"></honua-scene-inspector>
<honua-scene-measure for="#scene"></honua-scene-measure>
```

`metadata-url` carries the control content (layers/bookmarks/timeline/
compare/inspector); `tileset-url`, `terrain-url`, or a configured offline
package still has to be set for the primary 3D Tiles dataset to load.

See the [Scene Controls guide](../../docs/guides/scene-controls.md) for the
metadata schema, control composition recipes, and the full typed event
surface. The local `examples/scene-construction/` demo wires all six controls
together against a public CesiumGS sample tileset.

## Generated Map Snippets

```ts
import { createHonuaMapSnippet } from '@honua-io/embed';

const snippet = createHonuaMapSnippet({
  serviceUrl: 'https://services.honua.example/FeatureServer',
  layerIds: ['assets', 'work-orders'],
  center: { latitude: 21.3069, longitude: -157.8583 },
  zoom: 12,
  interactive: true,
  search: true,
  identify: true,
  label: 'City asset map',
  style: {
    accent: '#0f766e',
    fontFamily: 'Aptos, sans-serif',
  },
}, {
  elementName: 'city-asset-map',
});
```

Custom element names generate a script that calls `defineHonuaMapElement(...)`.
`apiKey` is omitted unless `includeCredentials: true` is passed.

For CDN distribution, use `createHonuaMapCdnSnippet`. It emits the CDN module
script and the same white-label custom element markup. When a custom
`elementName` is provided, the snippet imports `defineHonuaMapElement(...)` from
the CDN bundle and registers that branded tag name.

```ts
import { createHonuaMapCdnSnippet } from '@honua-io/embed/snippets';

const snippet = createHonuaMapCdnSnippet({
  serviceUrl: 'https://services.honua.example/FeatureServer',
  layerIds: ['assets'],
  interactive: true,
  label: 'City asset map',
}, {
  scriptUrl: 'https://cdn.honua.dev/embed.js',
  scriptAttributes: {
    integrity: 'sha384-...',
    crossOrigin: 'anonymous',
  },
});
```

Server-side builders that only generate markup can import from
`@honua-io/embed/snippets` to avoid loading the browser custom element
entrypoint.

For hosts that cannot load custom elements, use `createHonuaMapIframeSnippet`.
Map options are encoded into the iframe URL query string, with `apiKey` omitted
unless `includeCredentials: true` is passed. The package build includes the
static fallback shell at `dist/embed/map.html`; CDN deployments can serve that
file as `https://cdn.honua.dev/embed/map.html`.

```ts
import { createHonuaMapIframeSnippet } from '@honua-io/embed/snippets';

const snippet = createHonuaMapIframeSnippet({
  serviceUrl: 'https://services.honua.example/FeatureServer',
  layerIds: ['assets'],
  search: true,
  identify: true,
  label: 'City asset map',
}, {
  iframeUrl: 'https://cdn.honua.dev/embed/map.html',
  parentOrigin: 'https://portal.example.com',
  iframe: {
    title: 'City asset map',
  },
});
```

The iframe shell imports `../iframe.js`, hydrates a full-frame `<honua-map>`
from the query string, and forwards `honua-map-ready`,
`honua-map-config-change`, `honua-map-search`, and `honua-map-identify` to the
parent window with `{ source: 'honua-map-iframe', version: 1, type, detail }`.
Set `parentOrigin` when generating snippets so forwarded messages are scoped to
the embedding application origin.

Use `applyHonuaMapOptions(element, options)` to apply the same options shape to
an existing map element at runtime.

## Host Extensions

```ts
import { registerHonuaEmbedExtension } from '@honua-io/embed';

const registration = registerHonuaEmbedExtension({
  id: 'isv-locate',
  target: 'map',
  activate(context) {
    context.addControl({
      id: 'locate',
      label: 'Locate asset',
      text: 'L',
      onClick: (_event, clickContext) => {
        clickContext.dispatch('isv-locate', {
          zoom: clickContext.config.zoom,
        });
      },
    });
  },
});
```

Extensions are runtime host hooks for controls, CSS variables, and DOM events.
Shared plugin manifests, source descriptors, and data contracts should come from
versioned `Honua.Sdk.*` packages.

## Styling

The component uses Shadow DOM and exposes CSS custom properties:

```css
honua-map {
  --honua-map-accent: #1f7a8c;
  --honua-map-surface: #ffffff;
  --honua-map-border: rgba(19, 33, 44, 0.16);
  --honua-map-font-family: Inter, sans-serif;
}

honua-scene {
  --honua-scene-accent: #4fb4c8;
  --honua-scene-background: #101820;
  --honua-scene-font-family: Inter, sans-serif;
}
```

This package provides embeddable APIs, accessibility shells, theming, and integration events. Follow-on work can connect the 2D map surface to Honua feature/query endpoints and wire `<honua-scene>` to Honua-hosted scene registries, terrain services, and generated 3D Tiles.
