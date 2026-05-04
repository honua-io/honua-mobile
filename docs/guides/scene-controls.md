# Scene Controls

`@honua-io/embed` ships a set of reusable control surfaces that compose with
`<honua-scene>` to give Honua its operational 3D-workspace feel. Each control
is a framework-agnostic web component that consumes a shared
`HonuaSceneMetadata` document and emits typed `honua-scene-*` events the JS
SDK and app shells can synchronize against without importing CesiumJS.

## What you get

| Element | Purpose |
| --- | --- |
| `<honua-scene-layers>` | Layer switcher with visibility + opacity. |
| `<honua-scene-bookmarks>` | Saved camera framings. |
| `<honua-scene-timeline>` | Phase-based playback that toggles layers. |
| `<honua-scene-compare>` | Side picker between left/right layer sets. |
| `<honua-scene-inspector>` | Feature attribute panel populated from picks. |
| `<honua-scene-measure>` | Point/distance/area measurement tools. |

The components register automatically when you `import '@honua-io/embed'`. They
can also be defined individually with `defineHonuaSceneLayersElement`, etc., or
all at once with `defineHonuaSceneControls()`.

## Composition

Controls bind to a `<honua-scene>` via the `for` attribute (any CSS selector
works) or, when omitted, by walking back through previous siblings. App shells
are free to lay out controls outside the scene viewport — sidebars, panels,
floating cards.

```html
<honua-scene id="scene" metadata-url="/scenes/site-42.json"></honua-scene>
<aside>
  <honua-scene-layers for="#scene"></honua-scene-layers>
  <honua-scene-bookmarks for="#scene"></honua-scene-bookmarks>
  <honua-scene-timeline for="#scene"></honua-scene-timeline>
  <honua-scene-compare for="#scene"></honua-scene-compare>
  <honua-scene-inspector for="#scene"></honua-scene-inspector>
  <honua-scene-measure for="#scene"></honua-scene-measure>
</aside>
```

The local `examples/scene-construction/` demo wires all six controls with a
construction-themed metadata fixture and runs without AWS, Azure, or a Cesium
ion token.

## Scene metadata

`<honua-scene>` accepts metadata via either the `metadata-url` attribute (the
component fetches and parses the document) or the `metadata` property
(programmatic). Both paths surface a typed `HonuaSceneMetadata` value to every
bound control.

The metadata shape is `honua-scene-metadata/v1`. It is a forward-compatible
superset of the SDK fixture at
`tests/Honua.Mobile.Sdk.Tests/Fixtures/Scenes/scene-metadata.json` — the
familiar `id`, `name`, `description`, `center`, `bounds`, `tileset`, `terrain`,
`capabilities`, and `links` fields stay where they are. The new optional
fields drive the controls:

```jsonc
{
  "schema": "honua-scene-metadata/v1",
  "id": "site-42",
  "name": "Site 42",
  "layers": [
    { "id": "as-built", "title": "As-built capture", "kind": "3d-tiles", "url": "https://…/asbuilt.json" },
    { "id": "design-overlay", "title": "Design overlay", "kind": "3d-tiles", "url": "https://…/design.json", "opacity": 0.85 }
  ],
  "bookmarks": [
    {
      "id": "site-overview",
      "title": "Site overview",
      "view": {
        "center": { "latitude": 39.95, "longitude": -75.16 },
        "height": 2400,
        "orientation": { "heading": 0, "pitch": -45, "roll": 0 }
      }
    }
  ],
  "timeline": {
    "phases": [
      {
        "id": "phase-1",
        "title": "Foundation",
        "startUtc": "2026-01-06T00:00:00Z",
        "endUtc": "2026-03-30T00:00:00Z",
        "visibleLayerIds": ["as-built"]
      }
    ]
  },
  "compare": {
    "modes": [
      {
        "id": "design-vs-asbuilt",
        "title": "Design vs As-built",
        "leftLayerIds": ["design-overlay"],
        "rightLayerIds": ["as-built"]
      }
    ]
  },
  "inspector": {
    "fields": [
      { "key": "id", "title": "Asset ID" },
      { "key": "phase", "title": "Phase" },
      { "key": "elevation", "title": "Elevation", "format": "number", "unit": "m" }
    ]
  }
}
```

Validation surfaces a `metadata-invalid` code with a JSONPath-style `path` for
the offending field on a `honua-scene-metadata-error` event:

```ts
scene.addEventListener('honua-scene-metadata-error', (event) => {
  console.warn(event.detail.code, event.detail.path, event.detail.message);
});
```

A `metadata-fetch-failed` code is emitted when the network request fails or
returns a non-2xx status.

## Imperative scene API

`HonuaSceneElement` exposes a small layer API the controls (and host code) can
call directly:

| Method | Description |
| --- | --- |
| `applyView(view)` | Wraps `setView` for a `HonuaSceneViewSpec`. |
| `addLayer(metadata)` | Registers a metadata-shaped layer; loads the tileset when the scene is live. |
| `removeLayer(id)` | Removes a registered layer and any backing tileset. |
| `setLayerVisibility(id, visible)` | Toggles visibility; emits `honua-scene-layer-change`. |
| `setLayerOpacity(id, opacity)` | Sets the layer opacity (0–1); emits `honua-scene-layer-change`. |
| `getLayer(id)` | Returns the live layer handle (metadata + visibility/opacity + tileset). |

Layers declared in `metadata.layers` are tracked under their declared `id`.
The implicit `tileset-url` becomes the `id: "primary"` layer when no metadata
overrides it, so timelines and compare modes can refer to it by name.

## Typed events

All control events bubble and compose. None of them leak Cesium types, so the
JS SDK can model them without importing Cesium.

| Event | Detail |
| --- | --- |
| `honua-scene-metadata-change` | `{ metadata, source: 'attribute'\|'property'\|'fetch'\|'cleared' }` |
| `honua-scene-metadata-error` | `{ url, code, message, path?, error? }` |
| `honua-scene-layer-change` | `{ layerId, reason, layer, visible, opacity }` |
| `honua-scene-layer-toggle` | `{ layerId, visible, controlId: 'layers' }` |
| `honua-scene-layer-opacity` | `{ layerId, opacity, controlId: 'layers' }` |
| `honua-scene-bookmark-apply` | `{ bookmarkId, view, controlId: 'bookmarks' }` |
| `honua-scene-timeline-change` | `{ phaseId, startUtc, endUtc, visibleLayerIds, controlId: 'timeline' }` |
| `honua-scene-compare-set` | `{ modeId, side, leftLayerIds, rightLayerIds, controlId: 'compare' }` |
| `honua-scene-feature-select` | `{ featureId, attributes, controlId: 'inspector' }` |
| `honua-scene-measurement-add` | `{ measurementId, kind, points, distance?, area?, controlId: 'measure' }` |
| `honua-scene-measurement-clear` | `{ measurementId, controlId: 'measure' }` |
| `honua-scene-control-error` | `{ controlId, kind, message, error? }` |

Existing scene events (`honua-scene-ready`, `honua-scene-config-change`,
`honua-scene-load-error`, `honua-scene-camera-change`,
`honua-scene-identify`) keep their current contract.

## Running the demo

From the repository root:

```bash
npm ci --prefix src/Honua.Embed
npm run build --prefix src/Honua.Embed
python3 -m http.server 8080
```

Then open `http://localhost:8080/examples/scene-construction/`. The default
fixture references public CesiumGS 3D Tiles samples, so no Honua, AWS, Azure,
or Cesium ion credentials are required for the local demo. App shells that
need a fully offline tileset can pair the controls with the existing
[`packageAssetResolver`](3d-scene-embed.md#offline-package-resolver) flow —
the metadata document is independent of asset fetch policy.
