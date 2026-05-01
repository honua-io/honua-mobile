# Embeddable Map Component

`@honua/embed` provides a framework-agnostic `<honua-map>` custom element for ISV and SaaS integrations.

```html
<script type="module">
  import '@honua/embed';
</script>

<honua-map
  service-url="https://services.honua.example/FeatureServer"
  layer-ids="assets,work-orders"
  center="21.3069,-157.8583"
  zoom="12"
  interactive
  search
  identify>
</honua-map>
```

The component is white-label by default: it does not render Honua branding unless an integrator provides their own attribution. Host applications can style it with CSS custom properties without leaking styles into the map internals.

## Integration Events

```js
const map = document.querySelector('honua-map');

map.addEventListener('honua-map-search', (event) => {
  console.log(event.detail.query);
});

map.addEventListener('honua-map-identify', (event) => {
  console.log(event.detail.x, event.detail.y);
});
```

## Web Display Adapter

For production map rendering, host the base map with MapLibre GL JS and attach
Honua feature overlays through deck.gl. The adapter consumes renderer-neutral SDK
source descriptors and `FeatureQueryResult` pages; it does not define new query
contracts in this repository.

```js
import maplibregl from 'maplibre-gl';
import {
  HonuaWebDisplayAdapter,
  createHonuaGeoJsonLayer,
  featureQueryResultToGeoJson,
} from '@honua-io/embed';

const map = new maplibregl.Map({
  container: 'map',
  style: 'https://tiles.example/styles/streets.json',
  center: [-157.8583, 21.3069],
  zoom: 12,
});

const display = new HonuaWebDisplayAdapter(map);
const page = await sdk.features.queryFeatures(sourceDescriptor.id, query);

display.setFeatureQueryResult(page, {
  source: sourceDescriptor,
  onClick: ({ object }) => {
    console.log(object?.properties);
  },
});
```

Use MapLibre GL JS for base map, style, camera, vector-tile styles, and normal
map controls. Use deck.gl layers for high-volume overlays, picking,
highlighting, paths, polygons, point clouds, heatmaps, temporal animation, and
GPU aggregation. The initial implementation is a GeoJSON flow; binary deck.gl
attribute batches should be added only when feature volume requires them.

The pure converter is also exported when a host app owns the overlay lifecycle:

```js
const featureCollection = featureQueryResultToGeoJson(page);
const layer = createHonuaGeoJsonLayer(featureCollection, {
  id: 'honua-work-orders',
});
```

## Current Scope

`<honua-map>` still provides the declarative, white-label web component shell,
Shadow DOM encapsulation, theme hooks, accessible controls, search events, and
identify events. Production map rendering should use the MapLibre/deck.gl
adapter above until the custom element owns a full renderer lifecycle.

For 3D Tiles and CesiumJS-based scenes, use the [`<honua-scene>` guide](3d-scene-embed.md).
