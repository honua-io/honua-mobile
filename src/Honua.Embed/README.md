# @honua/embed

Framework-agnostic web component for embedding Honua map views in SaaS and ISV applications.

## Install

```bash
npm install @honua/embed
```

## Use

```html
<script type="module">
  import '@honua/embed';
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

## Attributes

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

## Events

| Event | Detail |
| --- | --- |
| `honua-map-ready` | Current `HonuaMapConfig`. |
| `honua-map-config-change` | Current `HonuaMapConfig`. |
| `honua-map-search` | `{ query, config }`. |
| `honua-map-identify` | `{ x, y, config }`. |

## Styling

The component uses Shadow DOM and exposes CSS custom properties:

```css
honua-map {
  --honua-map-accent: #1f7a8c;
  --honua-map-surface: #ffffff;
  --honua-map-border: rgba(19, 33, 44, 0.16);
  --honua-map-font-family: Inter, sans-serif;
}
```

This first slice provides the embeddable API, accessibility shell, theming, and integration events. A future slice can connect the visual surface to a full map renderer and Honua feature/query endpoints.
