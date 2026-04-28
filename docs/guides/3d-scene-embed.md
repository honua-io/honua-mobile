# 3D Scene Embed

`@honua/embed` includes a CesiumJS-backed `<honua-scene>` custom element for loading external or Honua-hosted 3D Tiles datasets.

```html
<script type="module">
  import '@honua/embed';
</script>

<honua-scene
  tileset-url="https://example.com/tileset.json"
  center="21.3069,-157.8583"
  height="1800"
  heading="20"
  pitch="-35">
</honua-scene>
```

CesiumJS is Apache-2.0 open source. The component does not require Cesium ion for external or Honua-hosted 3D Tiles URLs. Integrators can pass `ion-token` only when they choose to use Cesium ion assets or services.

## SDK Discovery

Mobile and web hosts can resolve scene metadata through `HonuaSceneService` before assigning URLs to the renderer.

```csharp
using Honua.Mobile.Sdk.Scenes;

var scene = await client.Scenes.ResolveSceneAsync(
    "downtown-honolulu",
    new HonuaSceneResolveRequest
    {
        RequiredCapabilities = new[] { HonuaSceneCapabilities.ThreeDimensionalTiles },
    });
```

```js
const element = document.querySelector('honua-scene');
const resolvedScene = await fetch('/scene-config/downtown-honolulu').then((response) => response.json());

element.setAttribute('tileset-url', resolvedScene.tilesetUrl);

if (resolvedScene.terrainUrl) {
  element.setAttribute('terrain-url', resolvedScene.terrainUrl);
}
```

## Events

```js
const scene = document.querySelector('honua-scene');

scene.addEventListener('honua-scene-ready', (event) => {
  console.log(event.detail.config);
});

scene.addEventListener('honua-scene-load-error', (event) => {
  console.error(event.detail.source, event.detail.message);
});

scene.addEventListener('honua-scene-identify', (event) => {
  console.log(event.detail.x, event.detail.y, event.detail.picked);
});
```

## Asset Packaging

The package build copies Cesium `Assets`, `Workers`, `ThirdParty`, and `Widgets` into `dist/cesium`. By default, `<honua-scene>` resolves Cesium runtime assets relative to the built `dist` module. Use `cesium-base-url` when hosting those assets from a CDN or another static path.

## Current Scope

This first slice proves client-side 3D Tiles loading, scene events, and typed SDK scene discovery. Honua-hosted scene registry, terrain tiles, elevation APIs, 3D Tiles generation, and I3S compatibility are tracked in the linked server backlog.

Use the [mobile 3D and AR dependency matrix](mobile-3d-ar-dependency-matrix.md) before starting native AR/VR or offline 3D work. It captures the required server tickets, SDK tickets, platform risks, offline constraints, and edition gates for each client capability.

For protected Honua-hosted scenes, resolve renderer-safe signed URLs through
`HonuaSceneService` before assigning `tileset-url` or `terrain-url`. Do not pass
bearer tokens, API keys, or arbitrary request headers into `<honua-scene>`
attributes. See [Protected 3D Scene Auth](protected-3d-scene-auth.md).
