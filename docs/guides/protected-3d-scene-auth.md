# Protected 3D Scene Auth Handoff

Protected 3D Tiles and terrain assets need a renderer-safe auth handoff because
scene renderers fetch many nested resources after the entry `tileset.json` or
terrain provider URL is loaded. Browser and WebView renderers cannot rely on the
same request-header hooks as native HTTP clients, and credentials must not be
leaked into markup or logs.

## Recommendation

Use a hybrid strategy:

1. Public scenes use public HTTPS URLs with deterministic `ETag` and
   `Cache-Control` headers.
2. Protected browser and WebView scenes use short-lived signed URLs or a signed
   asset prefix returned by `IHonuaSceneClient.ResolveSceneAsync`.
3. Native runtimes may use request headers only when the client explicitly
   declares that nested renderer asset requests support headers.
4. A first-party proxy is a fallback for strict tenant policy, audit, or
   revocation requirements, not the default path.

Do not pass bearer tokens, API keys, refresh tokens, or arbitrary auth headers
to `<honua-scene>` attributes. The renderer receives only URLs that are safe to
render, plus non-secret expiry/cache metadata for host UI decisions.

## Strategy Comparison

| Strategy | Browser/WebView fit | Native fit | Pros | Risks | Recommendation |
|----------|---------------------|------------|------|-------|----------------|
| Public URL | High | High | Simple, cacheable, works with Cesium nested fetches | Only valid for public scenes | Use for public scenes. |
| Short-lived signed URL or signed asset prefix | High | High | Works with nested renderer fetches, CDN-compatible, no bearer token in markup | URL sharing until expiry; refresh required | Default for protected scenes. |
| First-party proxy URL | Medium | Medium | Central audit, fast revocation, hides origin storage layout | Higher server cost, latency, CDN complexity | Fallback for strict policy or tenants that cannot use signed URLs. |
| Request-header injection | Low | Medium | Avoids URL bearer material and can use normal SDK auth | Browser/Cesium nested fetch support is inconsistent; easy to leak headers into client code | Native-only opt-in, never browser default. |
| Cookie-only session | Low | Low | Familiar web auth model | Third-party cookie blocking, WebView differences, CSRF and origin complexity | Avoid for embeddable scene assets. |

## Access Envelope

The scene resolve API should return an access envelope for each protected
endpoint. #44 tracks mobile SDK contracts for this shape, and
honua-io/honua-server#849 tracks server support.

```json
{
  "sceneId": "downtown-honolulu",
  "tilesetUrl": "https://cdn.honua.example/scenes/downtown/tileset.json?sig=...",
  "terrainUrl": "https://cdn.honua.example/scenes/downtown/terrain?sig=...",
  "expiresAt": "2026-04-28T18:30:00Z",
  "auth": {
    "requiresAuthentication": true,
    "schemes": ["SignedUrl"],
    "policy": "scene-render-protected"
  },
  "access": {
    "mode": "signed-url",
    "refreshAfterUtc": "2026-04-28T18:20:00Z",
    "expiresAtUtc": "2026-04-28T18:30:00Z",
    "corsMode": "registered-origins",
    "cache": {
      "public": false,
      "maxAgeSeconds": 300,
      "staleWhileRevalidateSeconds": 60
    },
    "customHeadersAllowed": false,
    "revocationKey": "scene-rev-42"
  }
}
```

Required access fields:

| Field | Requirement |
|-------|-------------|
| `mode` | One of `public`, `signed-url`, `proxy`, or `headers`. |
| `expiresAtUtc` | Hard stop for using signed/proxy access. |
| `refreshAfterUtc` | Host should resolve the scene again before this time. |
| `corsMode` | Server CORS behavior, such as `public`, `registered-origins`, or `same-origin`. |
| `cache` | Client-visible cache policy for renderer and host decisions. |
| `customHeadersAllowed` | `true` only for native/header-capable runtimes. |
| `revocationKey` | Server revision or policy key that invalidates previously issued access. |

## Runtime Guidance

### Browser and WebView

- Resolve the scene through the SDK or host backend.
- Pass only `tileset-url` and `terrain-url` values from the resolved response to
  `<honua-scene>`.
- Refresh the scene before `refreshAfterUtc` during long sessions.
- Treat `expiresAtUtc` as a hard stop and unload protected assets after expiry.
- Do not put bearer tokens, API keys, or header JSON into DOM attributes.
- Prefer signed URL query strings or signed path prefixes over cookies.

```csharp
var resolvedScene = await client.Scenes.ResolveSceneAsync("downtown-honolulu");

if (resolvedScene.Access is { IsBrowserSafe: false })
{
    throw new InvalidOperationException("Scene access requires a native renderer.");
}

// Host apps pass only renderer-safe URLs to the web component.
var tilesetUrl = resolvedScene.TilesetUrl;
var terrainUrl = resolvedScene.TerrainUrl;
```

### MAUI

- Use `IHonuaSceneClient` as the shared access resolver.
- Prefer the same signed URL path as browser/WebView so MAUI WebView and native
  renderers behave consistently.
- Store refresh credentials in platform secure storage, not in renderer markup
  or package manifests.
- Clear resolved URLs on sign-out, account switch, tenant switch, or access
  revocation.

### Native SDKs

- Use signed URLs by default for parity with browser rendering.
- Use header mode only when the renderer owns every nested asset request and can
  attach headers consistently.
- Keep bearer/API-key auth in SDK transport code and never expose it as scene
  metadata intended for rendering.

## CORS, Cache, And CDN Rules

Protected scene assets should use these defaults:

- `Access-Control-Allow-Origin` must be restricted to registered app/embed
  origins for browser access; use `Vary: Origin` when origin-specific.
- Allow `GET`, `HEAD`, and `OPTIONS` for scene assets.
- Expose only non-secret headers needed by renderers and host code, such as
  `ETag`, `Cache-Control`, `Content-Length`, and `Content-Type`.
- Public scenes may use shared CDN caching with deterministic `ETag`.
- Protected signed URLs may use short CDN TTLs that do not exceed
  `expiresAtUtc`.
- Do not cache protected signed URL responses in a shared cache unless the
  signature scopes the asset, scene revision, tenant, and expiry.

## Expiry, Refresh, And Revocation

Access expiry is separate from offline package use:

- Online render URLs expire at `expiresAtUtc`.
- Hosts should refresh at `refreshAfterUtc` to avoid mid-session renderer
  failures.
- Server revocation should invalidate access by scene revision, tenant, user, or
  key rotation.
- Offline packages follow [Offline 3D Scene Packages](offline-3d-scene-packages.md)
  and must not treat online signed URL expiry as the offline use grant.

## Security Risks

| Risk | Mitigation |
|------|------------|
| Signed URL copied from logs or browser devtools | Keep TTL short, scope URL to tenant/scene/revision, avoid bearer material in URL, and scrub logs. |
| Protected assets cached beyond authorization | Bound CDN/browser max age by `expiresAtUtc` and purge on revocation where supported. |
| Header instructions exposed to browser code | Use signed URLs for browser/WebView; reserve header mode for native-only clients. |
| Cross-origin embed leaks access | Restrict CORS to registered origins and use `Vary: Origin`. |
| Mid-session expiry breaks rendering | Return `refreshAfterUtc` and let hosts re-resolve before expiry. |
| Offline package outlives entitlement | Enforce `offlineUseExpiresAtUtc` from the offline package manifest. |

## Implementation Follow-Ups

| Ticket | Scope |
|--------|-------|
| #44 | Add SDK scene access-envelope models and parsing for render-safe access metadata. |
| honua-io/honua-server#849 | Add server scene access envelopes with signed URL/proxy/header modes. |
| honua-io/honua-server#837 | Apply auth, CORS, and cache behavior while serving hosted 3D Tiles assets. |
| honua-io/honua-server#844 | Expose public/protected scene access configuration in the scene registry. |
