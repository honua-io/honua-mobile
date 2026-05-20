# @honua-io/embed

Framework-agnostic web components for embedding Honua map and 3D scene views in SaaS and ISV applications.

## Install

```bash
npm install @honua-io/embed
```

`cesium`, `maplibre-gl`, and the `@deck.gl/*` packages are declared as optional
peer dependencies and are not bundled into `dist/index.js`. Install only the
peers you actually use:

```bash
# 3D scenes (<honua-scene>)
npm install cesium

# 2D maps and the display adapter
npm install maplibre-gl @deck.gl/core @deck.gl/layers @deck.gl/mapbox
```

Hosts that only render `<honua-map>` can omit `cesium`, and hosts that only
render `<honua-scene>` can omit MapLibre/deck.gl.

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
```

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
});
scene.setAttribute('package-id', manifest.packageId);
scene.setAttribute('tileset-asset', primaryTileset.path);
scene.setAttribute('package-expires-at', manifest.offlineUseExpiresAtUtc);
```

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
| `center` | Initial latitude/longitude pair, for example `21.3069,-157.8583`. |
| `height` | Initial camera height in meters. |
| `heading` | Initial camera heading in degrees. |
| `pitch` | Initial camera pitch in degrees. |
| `roll` | Initial camera roll in degrees. |
| `autoload` | Set to `false`, `0`, or `no` to disable automatic loading. |
| `theme` | `light` or `dark`. |

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
| `honua-scene-identify` | `{ x, y, picked, config }`. |
| `honua-embed-extension-error` | `{ extensionId, target, lifecycle, error }`. |

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

Use `applyHonuaMapOptions(element, options)` to apply the same options shape to
an existing map element at runtime.

## Embed Builder State

Admin portals can use the builder helpers to power a live configurator without
hand-assembling layer selections, preview attributes, validation, and snippets.

```ts
import {
  applyHonuaMapBuilderState,
  createHonuaMapBuilderState,
} from '@honua-io/embed';

const state = createHonuaMapBuilderState({
  serviceUrl: 'https://services.honua.example/FeatureServer',
  availableLayers: [
    { id: 'assets', label: 'Assets', defaultSelected: true },
    { id: 'work-orders', label: 'Work orders' },
  ],
  selectedLayerIds: ['assets', 'work-orders'],
  center: { latitude: 21.3069, longitude: -157.8583 },
  zoom: 12,
  interactive: true,
  search: true,
  identify: true,
  label: 'City asset map',
}, {
  elementName: 'city-asset-map',
});

applyHonuaMapBuilderState(document.querySelector('honua-map'), state);
console.log(state.snippet);
```

`state.issues` contains warning/error objects suitable for form validation.
Credentials are omitted from generated snippets unless `includeCredentials` is
explicitly enabled in snippet options.

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

## Cesium Ion Token Scope

`Cesium.Ion.defaultAccessToken` is a module-level global. Only one ion token can
be active per page at a time. When multiple `<honua-scene>` elements are
mounted concurrently with different `ion-token` attributes, the most recently
mounted element wins and a `console.warn` is emitted. Removing the `ion-token`
attribute releases that element's claim on the global but does not clear the
underlying token string, so other mounted widgets continue to work.

## Content Security Policy

The web components render inline `<style>` blocks inside their shadow roots.
Hosts that ship a strict CSP must allow inline styles for the page, for example
by adding `style-src 'unsafe-inline'` or by serving a nonce-aware build.

When `<honua-scene>` is configured with a `cesium-base-url` pointing at a CDN
or asset host, that origin must be allow-listed in `script-src`,
`connect-src`, and `worker-src` so Cesium can load its Workers, Assets, and
Widgets bundles. Only `http(s):`, `blob:`, and same-origin paths are accepted
for `cesium-base-url`; unsafe schemes (`javascript:`, `data:`, etc.) are
ignored and a `console.warn` is logged.
