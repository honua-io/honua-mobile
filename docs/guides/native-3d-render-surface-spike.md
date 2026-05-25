# Native 3D Render Surface Spike

Issue: [honua-mobile#1199][mobile-1199] (parent epic [honua-server#530][server-530])

Last reviewed: 2026-05-25.

This spike evaluates a **native 3D render surface** for the MAUI mobile SDK,
beyond the current CesiumJS-in-WebView approach, so the team can support credible
field **AR overlays** and **offline native 3D** without depending on a browser
runtime for camera-pose compositing. It is a decision document plus follow-up
ticket split, not an implementation. It follows the same structure as the
[Mapsui native display adapter spike](../spikes/mapsui-native-display-adapter.md)
and respects the ownership boundary in [AGENTS.md](../../AGENTS.md): the SDK owns
scene discovery/auth/offline/contracts, this repo owns the MAUI runtime adapter,
and the **platform host owns the renderer**.

## Decision

**Recommendation: a thin native MAUI render surface composited *inside* the
ARKit/ARCore session, built on platform-native engines per OS — RealityKit
(iOS) and Filament (Android) — rendering a Honua-owned lightweight overlay
payload (points, polylines, simple meshes, labels, bounded extrusions), and
explicitly *not* a native 3D Tiles engine in v1.**

Keep CesiumJS-in-WebView (`<honua-scene>` + the MAUI WebView host) as the
authoritative **non-AR 3D Tiles / terrain viewer**. Do not try to replace it
with a native 3D Tiles renderer in this workstream. Add the native surface only
where the WebView cannot go: live camera compositing for AR and disconnected
native rendering of small scene-package overlays.

This is deliberately the smallest native footprint that unblocks the
[#225][mobile-225] AR/3D GA validation and the
[native scene anchoring](native-scene-anchoring-requirements.md) plan, which
already chose ARCore-first / ARKit-second and explicitly deferred "full Cesium
3D Tiles rendering" to "a native renderer ticket [if] justified". This spike
*is* that ticket's decision input, and the honest answer is: **a full native 3D
Tiles engine is not justified for v1; a native AR-composited overlay surface
is.**

### Why not a native 3D Tiles engine now

No candidate below renders OGC 3D Tiles natively, in-process, on both iOS and
Android, from .NET MAUI, today, without us writing and maintaining a tiler.
CesiumJS already does this well in the WebView. The field-AR gap is *camera
compositing and offline overlay rendering*, not 3D-Tiles fidelity. Building a
native 3D Tiles engine would be a multi-quarter effort that duplicates
`<honua-scene>` and is out of scope for a spike-driven prototype.

## Problem And Current WebView Limitation

Mobile 3D today is **CesiumJS running inside a MAUI `WebView`** via
`src/Honua.Embed`'s `<honua-scene>` web component. That surface is excellent for
discovery, inspection, terrain, and 3D Tiles preview, and it already has an
offline package resolver (`packageAssetResolver` / cache-storage). It is the
right non-AR baseline and should stay.

The WebView path cannot credibly do three things the field-AR product needs:

| Limitation | Why the WebView blocks it |
|------------|---------------------------|
| **AR camera compositing** | Field AR requires drawing geospatially-anchored overlays *registered to the live camera feed and device pose*. The anchoring path the team already chose ([native scene anchoring](native-scene-anchoring-requirements.md)) is **native ARKit/ARCore**, not WebXR (WebXR `immersive-ar` coverage is inconsistent across iOS/WebView/managed devices). A `WebView` cannot share an `ARSession`/`ARCore Session` camera texture and pose stream with native AR; you would have to render the overlay natively anyway. |
| **Offline native rendering** | `<honua-scene>` offline depends on browser cache-storage / service-worker behavior, which iOS and managed Android WebViews purge unpredictably (documented in [Offline 3D Scene Packages](offline-3d-scene-packages.md) > Platform Risks). Native rendering can read validated package assets straight from app-controlled storage with no WebView cache lifetime risk. |
| **Performance / battery in AR** | A WebView + WebGL + Cesium tile pipeline composited against a 60fps camera feed is a heavy, hard-to-tune path. Native engines (RealityKit/Filament) are built to draw into an AR frame loop with predictable thermal/battery behavior. |

For non-AR, large 3D Tiles scenes the WebView is still the better answer — which
is exactly why this recommendation keeps it.

## Architecture Boundary (unchanged)

This spike does not move the boundary; it adds a renderer behind the existing
host seam.

- **SDK (`Honua.Sdk.*`, consumed as NuGet):** scene resolution
  (`IHonuaSceneClient`), tileset/terrain endpoint metadata, access envelopes,
  offline package manifest contracts (`HonuaScenePackageManifest`), and the
  lightweight overlay/feature payloads. No renderer code.
- **This repo (`Honua.Mobile.Maui`):** the AR session lifecycle and anchoring
  policy already live in `SceneAnchoring`
  (`IHonuaNativeArSceneAnchorAdapter`, `HonuaNativeArSceneAnchoringController`,
  `HonuaNativeArFieldWorkflow`, readiness/evidence contracts). The native render
  surface is a **new sibling concern** that consumes the same calibration
  transform and draws the SDK overlay payload. It must not redefine scene
  metadata, geometry, CRS, or package contracts.
- **Platform host / app:** owns the concrete `IHonuaNativeArSceneAnchorAdapter`
  (ARKit/ARCore) and now also owns the concrete renderer handler, registered via
  DI. The SDK ships interfaces and the shared MAUI shell; the app supplies the
  native implementation, exactly as the anchoring spike already requires for the
  AR adapter itself.

The clean insertion point: extend the anchoring adapter (or add a peer
`IHonuaNativeSceneRenderSurface`) so that once a session reaches a readiness
level, the host hands the renderer (a) the camera-to-world transform from
calibration and (b) the resolved overlay payload, and the renderer draws into
the AR frame. The same renderer can run **without** a camera in a plain native
3D view for offline non-AR preview of package overlays.

## Candidate Evaluation

Criteria: native 3D Tiles rendering *today*, terrain, AR overlay compositing
(ARKit/ARCore), offline, MAUI integration cost, licensing, maturity.

### Summary matrix

| Candidate | 3D Tiles native today | Terrain | AR compositing (ARKit/ARCore) | Offline native | MAUI integration cost | License | Maturity |
|-----------|-----------------------|---------|-------------------------------|----------------|-----------------------|---------|----------|
| **(b) Platform-native: RealityKit/SceneKit (iOS) + Filament (Android)** ⭐ | No (we draw overlay payload, not 3D Tiles) | Via overlay/elevation samples; no native tiled terrain engine | **Yes — designed for it.** RealityKit composites in `ARView`; Filament integrates with ARCore camera/pose | **Yes** — reads package assets from app storage | **Medium-high** — two native handlers + a thin shared abstraction, but reuses existing AR adapter seam | Apache-2.0 (Filament); RealityKit/SceneKit are platform SDKs (no extra license) | High on each platform individually |
| **(a) Filament on both iOS and Android** | No (same as above) | Same | Android: native ARCore path is well-trodden. iOS: Filament runs via Metal but ARKit compositing is **DIY and less proven** | Yes | **High** — one engine but two custom AR bridges, iOS ARKit bridge is the risky part | Apache-2.0 | High on Android, lower for the iOS-ARKit combination |
| **(c) MAUI `GraphicsView` / SkiaSharp GL path** | No | No | **No** — SkiaSharp is 2D/2.5D; no real 3D scene graph, no native AR camera compositing | Partial (could draw cached 2.5D) | Low to build, but **wrong tool** for 3D + AR | MIT (SkiaSharp) | Mature as a 2D surface, immature/absent for 3D+AR |
| **(d) Stay WebView + WebXR** | **Yes (CesiumJS)** — best 3D Tiles fidelity | Yes (Cesium terrain) | **No, not reliably** — WebXR `immersive-ar` coverage is inconsistent on iOS/WebView/managed devices; cannot share a native `ARSession` | Browser cache only (purge risk) | Low (already shipped) | Apache-2.0 (CesiumJS) | High for non-AR; low/uneven for field AR |

### Notes per candidate

**(b) Platform-native RealityKit (iOS) + Filament (Android) — recommended.**
This matches how Esri's native ARKit/ARCore AR overlays and tabletop AR are
built (native engine inside the platform AR session), and it matches the
team's already-accepted native-AR-first anchoring decision. RealityKit is the
modern Apple AR rendering stack (`ARView` does camera + lighting + occlusion
compositing for us; SceneKit is the older fallback). Filament is Google's
production PBR engine, Apache-2.0, widely used by mapping/AR SDKs, and integrates
cleanly with the ARCore camera texture and pose. The cost is two native handlers,
but the *shared* surface — overlay payload conversion, calibration transform,
readiness gating, evidence capture — is already modeled in `SceneAnchoring` and
stays in shared .NET. Neither engine renders 3D Tiles for us, which is fine: v1
draws the lightweight overlay payload, and 3D Tiles fidelity stays in the
WebView.

**(a) Filament on both platforms.** Tempting for "one engine," but the iOS side
loses RealityKit's turnkey ARKit compositing (occlusion, environment lighting,
people occlusion) and forces a custom Metal/ARKit bridge. More risk for marginal
code-sharing benefit, because the AR *session* code is platform-specific
regardless. Keep Filament for Android; prefer RealityKit on iOS.

**(c) MAUI `GraphicsView` / SkiaSharp GL.** SkiaSharp is a 2D/immediate-mode
canvas. There is no 3D scene graph, no depth/occlusion against a camera feed, and
no AR session integration. It is the wrong primitive for geospatially-anchored 3D
AR. Viable only for 2D HUD/annotation overlays drawn *on top of* the real
renderer, not as the render surface itself.

**(d) Stay WebView + WebXR.** Best for non-AR 3D Tiles — keep it for that. As the
*AR* runtime it fails the core requirement: it cannot reliably do
`immersive-ar` across the target device matrix and cannot share the native
`ARSession` camera/pose that the chosen anchoring path produces. The anchoring
spike already rejected WebXR as the first AR runtime for the same reasons.

## Recommendation And Effort

Adopt **(b)**: a thin native AR-composited render surface — RealityKit on iOS,
Filament on Android — drawing the Honua lightweight overlay payload, behind a new
shared `IHonuaNativeSceneRenderSurface` seam that reuses the existing
`SceneAnchoring` calibration/readiness/evidence model. Keep CesiumJS-in-WebView
as the non-AR 3D Tiles viewer. Defer any native 3D Tiles engine until a separate,
explicitly-justified epic with server-generated tilesets ([honua-server#842][server-842]).

Rough effort (engineering, excluding physical-device QA which is its own track):

| Slice | Rough effort |
|-------|--------------|
| Shared `IHonuaNativeSceneRenderSurface` abstraction + overlay payload binding + DI seam (shared .NET) | **~1–1.5 weeks** |
| Android Filament handler: ARCore camera/pose bind, draw points/lines/labels/extrusions, offline package assets | **~3–4 weeks** |
| iOS RealityKit handler: `ARView` integration, parity overlay rendering, optional LiDAR occlusion | **~3–4 weeks** |
| Offline non-AR native preview mode (same renderer, no camera) | **~1 week** |
| Field QA: device matrix, fixtures, thresholds (folds into [#225][mobile-225] / `quality(ar)` ticket) | tracked separately |

Total core engineering ~**8–10 weeks** for both platforms, sequenced
Android-first to match the anchoring plan. This is intentionally *much* smaller
than a native 3D Tiles engine because v1 renders the overlay payload, not tiles.

## Prototype

**No runnable native prototype was built in this spike — intentionally.** A
credible proof requires Xcode + RealityKit (macOS/device) or the Android SDK +
ARCore + Filament with a physical AR-capable device; none are available in this
environment, and forcing the native toolchains would burn the spike budget for
little decision value. This matches the precedent set by the
[Mapsui spike](../spikes/mapsui-native-display-adapter.md), which deferred its
prototype to an implementation slice.

Instead, the illustrative seam below shows the intended integration shape so the
first implementation ticket starts from a concrete contract rather than a blank
page. **This is illustrative pseudo-scaffold, not compiled code, and is not
checked into `src/`.** It deliberately reuses the existing `SceneAnchoring`
types.

```csharp
// ILLUSTRATIVE ONLY — not a compiled file. Shows the intended seam.
// Would live in Honua.Mobile.Maui.SceneRendering (shared), with platform
// handlers in the app/host (RealityKit on iOS, Filament on Android),
// registered via DI exactly like IHonuaNativeArSceneAnchorAdapter.

namespace Honua.Mobile.Maui.SceneRendering;

using Honua.Mobile.Maui.SceneAnchoring;

/// Lightweight, renderer-neutral overlay payload (sourced from SDK contracts,
/// e.g. honua-server#841 extruded features / overlay payload). The render
/// surface never resolves scene metadata or geometry itself.
public sealed record HonuaSceneOverlay
{
    public required string SceneId { get; init; }
    public IReadOnlyList<HonuaOverlayPoint> Points { get; init; } = [];
    public IReadOnlyList<HonuaOverlayPolyline> Polylines { get; init; } = [];
    public IReadOnlyList<HonuaOverlayExtrusion> Extrusions { get; init; } = [];
    public IReadOnlyList<HonuaOverlayLabel> Labels { get; init; } = [];
}

/// Platform-native render surface. RealityKit (iOS) / Filament (Android).
/// Consumes the calibration transform produced by the anchoring controller.
public interface IHonuaNativeSceneRenderSurface
{
    HonuaNativeArRuntime Runtime { get; }

    /// AR mode: draw the overlay registered to the live camera using the
    /// scene->AR-world transform from control-point calibration.
    ValueTask RenderArOverlayAsync(
        HonuaSceneOverlay overlay,
        HonuaSceneToArTransform transform,
        HonuaNativeArReadiness readiness,
        CancellationToken ct = default);

    /// Offline / non-AR mode: draw the same overlay in a plain native 3D view
    /// from validated package assets, no camera feed required.
    ValueTask RenderOfflinePreviewAsync(
        HonuaSceneOverlay overlay,
        CancellationToken ct = default);

    ValueTask ClearAsync(CancellationToken ct = default);
}
```

The first implementation ticket should turn this sketch into a real shared
abstraction + one platform (Android/Filament) handler that loads a small overlay
fixture, before any iOS work.

## Follow-Up Tickets

Create these as separate implementation tickets, sequenced Android-first to match
the [native scene anchoring](native-scene-anchoring-requirements.md) plan. These
extend, and do not duplicate, the existing `feat(ar-*)` ticket split from that
spike.

| Ticket title | Runtime | Scope (one line) |
|--------------|---------|------------------|
| `feat(render-maui): native scene render-surface abstraction + DI seam` | Shared MAUI | Define `IHonuaNativeSceneRenderSurface` + renderer-neutral `HonuaSceneOverlay` binding from SDK overlay payload, reusing `SceneAnchoring` calibration/readiness; register via DI like the AR adapter. |
| `feat(render-android): Filament AR overlay handler` | Android / MAUI handler | Bind Filament to the ARCore camera texture + pose, draw points/lines/labels/bounded extrusions registered by the calibration transform; load assets from validated offline packages. |
| `feat(render-ios): RealityKit AR overlay handler` | iOS / MAUI handler | Implement the same surface in an ARKit `ARView` via RealityKit, parity overlay rendering, optional LiDAR occlusion as a quality tier, matching Android confidence/readiness gating. |
| `feat(render-offline): non-AR native 3D preview mode` | Shared MAUI + platform handlers | Render the overlay payload in a plain native 3D view from app-controlled package storage (no camera), so offline preview does not depend on WebView cache lifetime. |
| `quality(render): native render-surface field validation fixtures` | QA / field validation | Overlay fixtures, device matrix, frame-rate/thermal/battery acceptance, and registration-accuracy checks; folds into the existing `quality(ar)` validation track and [#225][mobile-225]. |
| `spike(render-3dtiles): native 3D Tiles engine feasibility (deferred)` | Spike | Revisit a native 3D Tiles engine only if/when server-generated tilesets ([honua-server#842][server-842]) and a product need justify replacing the WebView for large native scenes. Not in this workstream. |

No Flutter ticket should be created for this workstream.

## Dependency Links

SDK dependencies (consume as versioned `Honua.Sdk.*` NuGet, do not recreate):

- [honua-sdk-dotnet#70][sdk-70]: SDK scene metadata and offline package contracts (closed).

Server dependencies to link as native rendering paths are implemented:

- [honua-server#837][server-837]: hosted 3D Tiles serving (scene asset roots).
- [honua-server#839][server-839]: terrain/elevation tiles (surface context).
- [honua-server#840][server-840]: elevation query/profile API (depth/terrain reconciliation).
- [honua-server#841][server-841]: extruded 3D feature output (the overlay payload v1 renders).
- [honua-server#842][server-842]: 3D Tiles generation (gates any future native 3D Tiles engine).
- [honua-server#844][server-844]: scene dataset registry (stable scene ids, bounds, attribution).
- [honua-server#849][server-849]: signed access envelope (protected native rendering).

Mobile relationships:

- [honua-mobile#225][mobile-225]: AR/3D GA physical-device validation — this render surface feeds it.
- [native scene anchoring requirements](native-scene-anchoring-requirements.md): the AR session/anchoring/calibration model this surface composites against.
- [offline 3D scene packages](offline-3d-scene-packages.md): the package/asset storage and expiry policy native rendering must honor.

## PR Body Recommendation

Include this line in the PR body:

```text
Related to #1199
```

Suggested summary:

```text
## Summary
- Documented the native 3D render-surface spike decision.
- Recommended a thin native AR-composited overlay surface (RealityKit on iOS,
  Filament on Android), not a native 3D Tiles engine, and kept CesiumJS-in-WebView
  as the non-AR 3D Tiles viewer.
- Drafted the follow-up implementation ticket split, Android-first.

## Validation
- Markdown validation; no native prototype built (toolchains unavailable, by design).
```

[mobile-1199]: https://github.com/honua-io/honua-mobile/issues/1199
[mobile-225]: https://github.com/honua-io/honua-mobile/issues/225
[server-530]: https://github.com/honua-io/honua-server/issues/530
[sdk-70]: https://github.com/honua-io/honua-sdk-dotnet/issues/70
[server-837]: https://github.com/honua-io/honua-server/issues/837
[server-839]: https://github.com/honua-io/honua-server/issues/839
[server-840]: https://github.com/honua-io/honua-server/issues/840
[server-841]: https://github.com/honua-io/honua-server/issues/841
[server-842]: https://github.com/honua-io/honua-server/issues/842
[server-844]: https://github.com/honua-io/honua-server/issues/844
[server-849]: https://github.com/honua-io/honua-server/issues/849
