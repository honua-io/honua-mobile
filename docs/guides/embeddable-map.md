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

Use the CDN helper when a portal needs to emit a standalone `<script>` tag
instead of an npm import. The default CDN URL is `https://cdn.honua.dev/embed.js`
and can be replaced for tenant-specific or pinned asset paths.

```js
import { createHonuaMapCdnSnippet } from '@honua-io/embed/snippets';

const snippet = createHonuaMapCdnSnippet({
  serviceUrl: 'https://services.honua.example/FeatureServer',
  layerIds: ['assets', 'work-orders'],
  interactive: true,
  identify: true,
  label: 'City asset map',
}, {
  scriptUrl: 'https://cdn.honua.dev/embed.js',
  scriptAttributes: {
    integrity: 'sha384-...',
    crossOrigin: 'anonymous',
  },
});
```

After building the package, create the CDN manifest from the generated `dist`
folder:

```bash
npm run build --prefix src/Honua.Embed
npm run cdn:manifest --prefix src/Honua.Embed
```

`dist/cdn-manifest.json` lists the package name/version, default URLs, and
top-level JavaScript files with byte size, SHA-256 hash, and SHA-384 SRI
integrity. It covers `embed.js`, `index.js`, `iframe.js`, `snippets.js`, and
top-level Vite chunks; Cesium runtime assets under `dist/cesium` are copied for
hosting but are not expanded into the manifest.
The publish workflow uploads the built `dist/` directory as a CDN artifact so
release operators can promote the static bundle separately from npm publishing.

When generating snippets for a CDN-hosted build, read the `embed.js` entry and
pass its integrity value through the existing helper:

```js
const manifest = await fetch('https://cdn.honua.dev/cdn-manifest.json').then((response) => response.json());
const embedEntry = manifest.files.find((file) => file.path === 'embed.js');

const snippet = createHonuaMapCdnSnippet(options, {
  scriptUrl: embedEntry.url,
  scriptAttributes: {
    integrity: embedEntry.integrity,
    crossOrigin: 'anonymous',
  },
});
```

The generated CDN markup stays white-label; it does not add Honua attribution.
When `elementName` is customized, the helper emits an inline module import that
registers the branded tag name from the CDN bundle.

Server-side builders that only generate markup can import from
`@honua-io/embed/snippets`; that subpath avoids loading the browser custom
element entrypoint.

Embed configurators can route a user-selected output target through
`createHonuaMapEmbedBuilderSnippet` instead of duplicating switch logic across
the individual helpers. The default target is `web-component`; supported map
targets are `web-component`, `cdn`, `iframe`, `react`, `react-iframe`, `vue`,
`vue-iframe`, `angular`, and `angular-iframe`.

```js
import { createHonuaMapEmbedBuilderSnippet } from '@honua-io/embed/snippets';

const snippet = createHonuaMapEmbedBuilderSnippet(options, {
  target: selectedTarget,
  cdn: {
    scriptUrl: 'https://cdn.honua.dev/embed.js',
    scriptAttributes: {
      integrity: embedEntry.integrity,
      crossOrigin: 'anonymous',
    },
  },
  iframe: {
    iframeUrl: 'https://cdn.honua.dev/embed/map.html',
    parentOrigin: 'https://portal.example.com',
  },
});
```

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

For hosts that cannot use web components, generate an iframe fallback with the
same option shape. Map options are serialized into the iframe URL query string,
and `apiKey` is omitted unless `includeCredentials: true` is set. The npm/CDN
build packages the fallback shell at `dist/embed/map.html`; the default snippet
URL assumes that file is hosted as `https://cdn.honua.dev/embed/map.html`.

```js
import { createHonuaMapIframeSnippet } from '@honua-io/embed/snippets';

const snippet = createHonuaMapIframeSnippet({
  serviceUrl: 'https://services.honua.example/FeatureServer',
  layerIds: ['assets', 'work-orders'],
  center: { latitude: 21.3069, longitude: -157.8583 },
  zoom: 12,
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

The fallback shell imports `../iframe.js`, creates a full-frame `<honua-map>`
from the query string, and forwards `honua-map-ready`,
`honua-map-config-change`, `honua-map-search`, and `honua-map-identify` to the
embedding window with `{ source: 'honua-map-iframe', version: 1, type, detail }`.
Set `parentOrigin` to constrain forwarded messages to the host application
origin. If a `parent-origin` query value is malformed, the iframe disables
parent event forwarding instead of falling back to `*`.

Browser hosts can consume those forwarded events through the typed iframe helper.
The `origin` filter is the iframe shell origin, while `parentOrigin` in the
generated snippet is the embedding application origin used by `postMessage`.

```js
import { addHonuaMapIframeMessageListener } from '@honua-io/embed/iframe';

const iframe = document.querySelector('#asset-map-frame');
const disconnect = addHonuaMapIframeMessageListener((message) => {
  if (message.type === 'honua-map-identify') {
    console.log(message.detail);
  }
}, {
  origin: 'https://cdn.honua.dev',
  source: iframe.contentWindow,
});
```

Hosts can also update iframe map options after initial load without rebuilding
the iframe `src`. Send a typed configure command to the iframe content window
and scope `targetOrigin` to the iframe shell origin.

```js
import { postHonuaMapIframeConfigure } from '@honua-io/embed/iframe';

const iframe = document.querySelector('#asset-map-frame');

postHonuaMapIframeConfigure(iframe.contentWindow, {
  basemap: 'satellite',
  search: true,
  style: {
    accent: '#334155',
  },
}, 'https://cdn.honua.dev');
```

The fallback shell accepts configure commands only from the window and origin
declared by `parentOrigin` and applies the update with `applyHonuaMapOptions`.
The command envelope uses `source: 'honua-map-host'`, `version: 1`,
`type: 'honua-map-configure'`, and `options`; `isHonuaMapIframeCommand` is
available for hosts that proxy or validate these messages.

Framework teams can generate typed starter components around the same
`<honua-map>` custom element or iframe fallback. `@honua-io/embed` does not
depend on React, Vue, or Angular; these helpers only return code strings and
reuse the same option serializers and credential omission defaults as the base
snippet helpers.

```js
import {
  createHonuaMapAngularSnippet,
  createHonuaMapReactIframeSnippet,
  createHonuaMapVueSnippet,
} from '@honua-io/embed/snippets';

const vueComponent = createHonuaMapVueSnippet({
  serviceUrl: 'https://services.honua.example/FeatureServer',
  layerIds: ['assets'],
  identify: true,
  label: 'City asset map',
});

const reactIframeComponent = createHonuaMapReactIframeSnippet({
  search: true,
  label: 'City asset map',
}, {
  componentName: 'CityAssetMapFrame',
  iframeUrl: 'https://cdn.honua.dev/embed/map.html',
  parentOrigin: 'https://portal.example.com',
});

const angularComponent = createHonuaMapAngularSnippet({
  basemap: 'streets',
  search: true,
}, {
  componentName: 'CityAssetMapComponent',
  selector: 'city-asset-map',
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

display.fitToSource(sourceDescriptor, { padding: 32, maxZoom: 15 });
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

The adapter also exposes a small layer lifecycle surface for host-owned map
screens:

```js
display.setView({
  center: { longitude: -157.8583, latitude: 21.3069 },
  zoom: 13,
});

display.setFeatureQueryResults([assetPage, workOrderPage], (_page, index) => ({
  source: index === 0 ? assetSource : workOrderSource,
}));

display.appendFeatureQueryResult(assetPage2, {
  source: assetSource,
});

const assets = display.getFeatureCollection('honua-assets');
display.removeLayer('honua-work-orders');
display.clearFeatureLayers();
```

`clearFeatureLayers()` only removes layers created by feature query and stream
helpers. Host-owned deck.gl layers passed to the adapter constructor remain in
place. When appending pages, pass the SDK source descriptor whenever the host is
loading more than one source; the adapter resolves the incoming result source
before falling back to its single cached feature layer.

Streaming feature feeds can use the same renderer-neutral source descriptor and
apply upsert/delete events into the adapter's GeoJSON layer state:

```js
display.setFeatureStreamEvent({
  type: 'upsert',
  sequence: event.sequence,
  source: sourceDescriptor,
  feature: event.feature,
});

display.setFeatureStreamEvent({
  type: 'delete',
  source: sourceDescriptor,
  objectIds: [event.objectId],
});
```

Converted features keep Honua picking metadata in `properties.__honua`,
including the source id, source descriptor, feature id, object id, and stream
sequence when present. Host apps can read that metadata from deck.gl picking
callbacks without binding renderer-neutral source descriptors to MapLibre or
deck.gl-specific contracts.

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
accessible controls, iframe fallback packaging, search events, and identify
events. Production map rendering should use the MapLibre/deck.gl adapter above
until the custom element owns a full renderer lifecycle. Follow-on work can add
feature loading, analytics, binary deck.gl attribute batches, admin embed-builder
screens, and framework-specific wrappers.

For 3D Tiles and CesiumJS-based scenes, use the [`<honua-scene>` guide](3d-scene-embed.md).
