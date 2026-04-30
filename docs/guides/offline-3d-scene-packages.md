# Offline 3D Scene Packages

This policy defines how Honua mobile clients should package, cache, validate,
and evict offline 3D scene assets. It extends the existing 2D map-area offline
model, but treats scene assets as immutable package content instead of editable
feature replicas.

The policy applies to 3D Tiles, terrain tiles, textures, scene metadata, and
elevation profile samples used by `<honua-scene>`, MAUI WebView hosts, and future
native AR/VR runtimes.

## Decision Summary

- Production offline 3D uses a server-produced package manifest.
- SDKs may support development-only direct dependency downloads, but production
  packages must be manifest-driven so nested tileset dependencies, auth expiry,
  attribution, byte budgets, and hashes are explicit.
- Local package use is controlled by `offlineUseExpiresAtUtc`; source URL or
  token expiry only controls download/refresh.
- A stale package can remain usable with a visible stale state until
  `offlineUseExpiresAtUtc`. An expired package must not render protected assets.
- Partial packages are never renderable. Required assets must validate before a
  package is marked ready.
- Client implementations are split across #40 shared manifest contracts, #41
  MAUI/.NET download and storage, and #42 browser/embed cache adapters.

## Package Contract

Every package has a UTF-8 JSON manifest named `manifest.json` at the package
root. The manifest is the only stable contract between server packaging and
client runtime storage.

The .NET SDK contract for #40 is `HonuaScenePackageManifest`, with
`HonuaScenePackageAsset`, `HonuaScenePackageAssetTypes`, and
`HonuaScenePackageManifestValidator` covering the policy created in #36. These
models are deliberately storage-neutral; #41 and #42 own downloader, catalog,
browser cache, and eviction behavior.

```json
{
  "schemaVersion": "honua.scene-package.v1",
  "packageId": "pkg_downtown_honolulu_2026_04",
  "sceneId": "downtown-honolulu",
  "displayName": "Downtown Honolulu 3D",
  "editionGate": "pro",
  "serverRevision": "scene-rev-42",
  "createdAtUtc": "2026-04-28T00:00:00Z",
  "staleAfterUtc": "2026-05-28T00:00:00Z",
  "offlineUseExpiresAtUtc": "2026-06-27T00:00:00Z",
  "authExpiresAtUtc": "2026-04-29T00:00:00Z",
  "extent": {
    "minLongitude": -157.872,
    "minLatitude": 21.293,
    "maxLongitude": -157.841,
    "maxLatitude": 21.319
  },
  "lod": {
    "minZoom": 12,
    "maxZoom": 17,
    "maxGeometricErrorMeters": 4.0
  },
  "byteBudget": {
    "maxPackageBytes": 2147483648,
    "declaredBytes": 987654321
  },
  "attribution": [
    "Honua",
    "City and County source data"
  ],
  "assets": [
    {
      "key": "scene-metadata",
      "type": "scene-metadata",
      "role": "metadata",
      "path": "metadata/scene.json",
      "contentType": "application/json",
      "bytes": 4832,
      "sha256": "base16-or-base64-hash",
      "etag": "\"scene-42\"",
      "required": true
    },
    {
      "key": "buildings-tileset",
      "type": "3d-tileset",
      "role": "primary-tileset",
      "path": "tilesets/buildings/tileset.json",
      "contentType": "application/json",
      "bytes": 10455,
      "sha256": "base16-or-base64-hash",
      "required": true
    },
    {
      "key": "terrain-12-742-1619",
      "type": "terrain-tile",
      "role": "terrain",
      "path": "terrain/12/742/1619.terrain",
      "contentType": "application/vnd.quantized-mesh",
      "bytes": 32984,
      "sha256": "base16-or-base64-hash",
      "required": false
    }
  ]
}
```

Required fields:

| Field | Requirement |
|-------|-------------|
| `schemaVersion` | Must be `honua.scene-package.v1` until a migration ticket defines v2. |
| `packageId` | Stable ID for the downloaded package. Used for local catalog, resume, and eviction. |
| `sceneId` | Matches the server scene ID returned by `IHonuaSceneClient`. |
| `editionGate` | Client-visible feature gate for package use: `community`, `pro`, or `enterprise`. |
| `serverRevision` | Changes whenever server scene content or required auth policy changes. |
| `createdAtUtc` | Server timestamp for package generation. |
| `extent` | WGS84 bounding box used for package selection and storage management. |
| `lod` | Zoom or geometric-error limits included in the package. |
| `byteBudget` | Server-declared maximum package size and expected bytes before download starts. |
| `staleAfterUtc` | Package may be rendered after this time, but UI must expose a stale state. |
| `offlineUseExpiresAtUtc` | Package must not render protected assets after this time without revalidation. |
| `authExpiresAtUtc` | Download or refresh credentials expire at this time. Public packages may set this to `null`; it must not be used as the offline-use grant. |
| `assets` | Complete list of package files with type, path, byte count, hash, and required flag. |

## Cacheable Asset Types

| Asset type | Purpose | Required in v1 |
|------------|---------|----------------|
| `scene-metadata` | Resolved scene metadata, capability flags, bounds, and attribution. | Yes |
| `3d-tileset` | Entry `tileset.json` for a 3D Tiles endpoint. | Yes when the scene has 3D Tiles |
| `3d-tile-content` | Nested b3dm, glb, gltf, subtree, binary, or texture payloads referenced by 3D Tiles. | Yes when referenced by a required tileset |
| `terrain-tile` | Terrain mesh or terrain-raster tiles. | Optional in v1 |
| `texture` | Shared textures not already embedded in tile content. | Optional |
| `elevation-profile` | Precomputed profile samples for known field paths. | Optional |
| `license-attribution` | Third-party license, source, and attribution files. | Yes when required by source data |

3D Tiles relative paths must be preserved under the package root. Renderers may
rewrite a package-local base URL, but they must not flatten nested references in
a way that changes Cesium or native renderer resolution behavior.

## Download And Resume Policy

Clients download assets in manifest order unless a runtime chooses a stricter
priority model. Required metadata and entry tilesets should be first so the
client can fail early before large optional assets begin.

Download behavior:

1. Fetch the manifest and validate `schemaVersion`, `sceneId`, `extent`, `lod`,
   `byteBudget`, and `offlineUseExpiresAtUtc`.
2. Reserve storage for `byteBudget.declaredBytes` when the platform allows it.
3. Download each asset to a temporary package directory.
4. Resume by asset key when the server supports HTTP range requests with stable
   `ETag` or content hash. If resume validation fails, restart that asset.
5. Verify bytes and `sha256` before moving an asset into the ready directory.
6. Mark the package ready only after all required assets validate.
7. Delete partial packages after repeated failure or explicit user cancellation.

Optional assets may fail without invalidating the package, but the local catalog
must record the missing keys so the renderer can avoid silent cache misses.

The .NET/MAUI runtime service for #41 is `IHonuaScenePackageDownloader`, backed
by `ScenePackageDownloader` in `Honua.Mobile.Offline.ScenePackages`. MAUI hosts
register it with `AddHonuaScenePackageDownload()` after
`AddHonuaGeoPackageOfflineSync(...)`. Downloaded packages are cataloged as
`ScenePackageRecord` rows, including package footprint bytes and
`MissingOptionalAssetKeys` for renderer fallback decisions.

## Invalidation And Expiry

Package state is derived from manifest fields and local validation:

| State | Meaning | Runtime behavior |
|-------|---------|------------------|
| `ready` | Required assets are present, hashes match, and package is not stale. | Render normally. |
| `stale` | `staleAfterUtc` has passed, but `offlineUseExpiresAtUtc` has not. | Render with stale status and refresh when connectivity returns. |
| `expired` | `offlineUseExpiresAtUtc` has passed. | Do not render protected assets; require revalidation or redownload. |
| `partial` | Required assets are missing or a download is incomplete. | Do not render; allow resume or cleanup. |
| `invalid` | Hash, byte budget, schema version, or required metadata validation failed. | Do not render; delete or redownload. |
| `revoked` | Server reports the package or scene revision is no longer valid. | Do not render; remove local package after user-visible notice. |

Auth policy:

- Tokens and signed URLs are download credentials, not local use grants.
- `authExpiresAtUtc` blocks further refresh attempts once expired.
- `offlineUseExpiresAtUtc` blocks local rendering of protected assets once
  expired, even if the files still exist on disk.
- Sign-out, account switch, or organization switch must purge protected scene
  packages unless the server explicitly marks the package as public.

## Storage And Eviction

Each runtime must enforce both a per-package byte budget and an app-level 3D
cache quota. The implementation tickets should expose these as configuration
instead of hard-coding a single storage size.

Eviction order:

1. Failed or partial packages.
2. Expired packages.
3. Revoked packages.
4. Stale packages that are not pinned by the user.
5. Least-recently-used ready packages.
6. Optional assets inside a ready package only when the package explicitly marks
   those assets as evictable.

Required assets inside a ready package should not be evicted independently. If a
required asset must be removed, the whole package moves to `partial` or is
deleted.

## Platform Risks

| Platform | Storage risk | Required mitigation |
|----------|--------------|---------------------|
| iOS | Background execution windows are short, and cache directories can be purged by the OS. | Store ready packages in app-controlled storage, use resumable background tasks where available, and keep partial packages recoverable. |
| Android | Device storage and vendor WebView behavior vary widely. | Enforce quotas before download, support resume, and avoid assuming WebView cache persistence. |
| Browser | Persistent storage is quota-limited and can be evicted by the browser. | Treat browser packages as best-effort unless persistent storage is granted; surface unsupported storage through scene events. |
| MAUI | The same API spans iOS, Android, Windows, and Mac Catalyst storage semantics. | Keep shared manifest/catalog behavior in .NET, but use platform-specific storage roots and cleanup hooks. |

## Security Requirements

- Do not store bearer tokens or long-lived credentials in `manifest.json`.
- Do not log signed URLs, package-local protected asset paths, or token-bearing
  download failures.
- Use platform secure storage for refresh credentials, not the package catalog.
- Treat 3D scene packages as protected data when the source scene is private.
- Remove protected packages on sign-out, tenant switch, or managed-device wipe.
- Preserve attribution and license files offline so exported screenshots or
  field reports can still show required source credits.

## Implementation Follow-Ups

| Ticket | Runtime scope |
|--------|---------------|
| #40 | Shared SDK manifest contracts and validation helpers. |
| #41 | MAUI/.NET downloader, local catalog, resume, storage quota, and eviction implementation. |
| #42 | Browser/WebView cache adapter for `<honua-scene>` package-local asset resolution. |

Server prerequisites remain tracked by honua-io/honua-server#837,
honua-io/honua-server#839, honua-io/honua-server#840,
honua-io/honua-server#842, and honua-io/honua-server#844.
