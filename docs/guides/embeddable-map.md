# Embeddable Map Component

`@honua-io/embed` provides a framework-agnostic `<honua-map>` custom element for ISV and SaaS integrations.

```html
<script type="module">
  import '@honua-io/embed';
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

## Generated Snippets

ISV portals can generate embed markup from the typed helper instead of assembling
attribute strings by hand.

```js
import { createHonuaMapSnippet } from '@honua-io/embed';

const snippet = createHonuaMapSnippet({
  serviceUrl: 'https://services.honua.example/FeatureServer',
  layerIds: ['assets', 'work-orders'],
  center: { latitude: 21.3069, longitude: -157.8583 },
  zoom: 12,
  interactive: true,
  search: true,
  identify: true,
  attribution: 'City GIS',
  label: 'City asset map',
  style: {
    accent: '#0f766e',
    fontFamily: 'Aptos, sans-serif',
  },
}, {
  elementName: 'city-asset-map',
});
```

When `elementName` is not `honua-map`, the generated module script calls
`defineHonuaMapElement('city-asset-map')` so the host can expose a branded tag
name while still using the same implementation. `apiKey` is omitted from
generated snippets unless `includeCredentials: true` is passed; generated markup
should only contain renderer-safe public credentials.

Runtime hosts can apply the same configuration shape to an existing element:

```js
import { applyHonuaMapOptions } from '@honua-io/embed';

applyHonuaMapOptions(document.querySelector('honua-map'), {
  basemap: 'satellite',
  search: true,
  style: {
    accent: '#334155',
  },
});
```

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

## Host Extensions

Host applications can register lightweight runtime extensions that mount
white-label controls into `<honua-map>` or `<honua-scene>` and react to config
changes. These are host UI/runtime extensions, not SDK-owned plugin manifests.

```js
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
  configChanged(context) {
    console.debug('map config changed', context.config);
  },
});

// Later, for teardown or tenant switch:
registration.unregister();
```

Extensions can set CSS custom properties through `context.setCssVariable(...)`,
dispatch composed DOM events through `context.dispatch(...)`, and return a
cleanup callback from `activate`. If an extension throws, the element emits
`honua-embed-extension-error` with the extension id, target, lifecycle, and
original error.

## Current Scope

`<honua-map>` provides the declarative, white-label web component shell, Shadow
DOM encapsulation, theme hooks, generated snippets, host extension controls,
accessible controls, search events, and identify events. Production map
rendering should use the MapLibre/deck.gl adapter above until the custom element
owns a full renderer lifecycle. Follow-on work can add feature loading,
analytics, binary deck.gl attribute batches, and framework-specific wrappers.

For 3D Tiles and CesiumJS-based scenes, use the [`<honua-scene>` guide](3d-scene-embed.md).
