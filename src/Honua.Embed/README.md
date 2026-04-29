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

## Scene Attributes

| Attribute | Purpose |
| --- | --- |
| `tileset-url` | External or Honua-hosted 3D Tiles `tileset.json` URL. |
| `terrain-url` | Optional Cesium terrain provider URL. |
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
| `honua-scene-load-error` | `{ source, message, config, error }`. |
| `honua-scene-camera-change` | `{ center, height, orientation, config }`. |
| `honua-scene-identify` | `{ x, y, picked, config }`. |

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
