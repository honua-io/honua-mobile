# Construction World Model Demo

A side-by-side scene + controls demo that loads a Honua scene metadata document
(`metadata.json`) and exercises the demo-critical controls: layers, bookmarks,
timeline phases, compare, feature inspector, and measurement.

The default fixture references public CesiumGS 3D Tiles samples, so it runs
without AWS, Azure, or a Cesium ion token.

## Run locally

From the repository root:

```bash
npm ci --prefix src/Honua.Embed
npm run build --prefix src/Honua.Embed
python3 -m http.server 8080
```

Then open `http://localhost:8080/examples/scene-construction/`.

The right-hand event log shows the typed `honua-scene-*` events as you
interact with the controls. Use the layer toggles, click a bookmark, switch
timeline phases, flip compare modes, click into the scene to inspect a
feature, and use the measure tool to drop point/line/polygon measurements.

## Wiring the controls

The demo binds controls to the scene with `for="#scene"`. You can also place a
control immediately after `<honua-scene>` to use the default sibling lookup.
The metadata document is loaded via `metadata-url` and parsed into a
`HonuaSceneMetadata` value that drives every control's UI.

To swap in your own scene, point `metadata-url` at any document that conforms
to `honua-scene-metadata/v1`. See
[`docs/guides/scene-controls.md`](../../docs/guides/scene-controls.md) for the
full schema and event reference.
