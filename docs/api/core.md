# Honua Mobile SDK API Reference

The Honua Mobile SDK ships as a set of focused .NET libraries. There is no
`Honua.Mobile.Core` namespace; this page links the real entrypoint and
namespaces.

## Entrypoint

The primary client class is:

```csharp
public sealed class Honua.Mobile.Sdk.HonuaMobileClient : IDisposable, IAsyncDisposable
```

`HonuaMobileClient` owns gRPC-first transport with REST fallback, auth
integration, scene metadata, and routing. See
[`src/Honua.Mobile.Sdk/HonuaMobileClient.cs`](../../src/Honua.Mobile.Sdk/HonuaMobileClient.cs)
for the public surface.

For DI-driven setup in MAUI apps, use the registration extensions in
`Honua.Mobile.Maui` rather than constructing the client directly. The root
[README QuickStart](../../README.md#quick-start) shows the canonical
`MauiProgram.cs` setup, including:

- `AddHonuaMobilePlatformAuth()`
- `AddHonuaMobileSdk(new HonuaMobileClientOptions { ... })`
- `AddHonuaRouting()`
- `AddHonuaScenes()`
- `AddHonuaMobileFieldCollection()`
- `AddHonuaSdkGeoPackageOfflineSync(...)` (preferred) or
  `AddHonuaGeoPackageOfflineSync(...)` (legacy)
- `AddHonuaBackgroundSync()`

## Packages and namespaces

| Package | Purpose |
|---------|---------|
| `Honua.Mobile.Sdk` | Transport, auth, gRPC-first client, REST fallback, routing, SDK scene metadata adapter |
| `Honua.Mobile.Field` | Mobile adapters for SDK-owned field forms, validation, media capture metadata, workflow |
| `Honua.Mobile.Offline` | GeoPackage storage, sync queue, map area download, conflict resolution |
| `Honua.Mobile.Maui` | MAUI service registration, DI extensions, native display, native scene anchoring, device location |

Reusable offline/journal/conflict/sync contracts live in `honua-sdk-dotnet`
(`Honua.Sdk.Abstractions` with the `Honua.Sdk.Offline.Abstractions` namespace,
plus `Honua.Sdk.Offline`); this repo supplies the mobile runtime adapters.

## Related guides

- [Offline sync](../guides/offline-sync.md)
- [Migration guide](../guides/migration-guide.md)
- [Troubleshooting](../guides/troubleshooting.md)
