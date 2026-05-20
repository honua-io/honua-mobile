# Honua Mobile SDK API Reference

The primary entrypoint for the Honua Mobile SDK is the
`Honua.Mobile.Sdk.HonuaMobileClient` class, a sealed transport client that
combines REST and gRPC access to Honua server APIs along with routing and
scene metadata helpers.

## Getting Started

See the **[QuickStart in the root README](../../README.md#quick-start)** for the
minimal MAUI service registration and a working `HonuaMobileClient` example. The
[installation guide](../getting-started/installation.md) covers per-platform
setup.

## Namespaces

| Namespace | Purpose |
|-----------|---------|
| `Honua.Mobile.Sdk` | `HonuaMobileClient`, transport options, auth helpers, REST/gRPC routing |
| `Honua.Mobile.Field` | Mobile adapters over SDK-owned field forms, validation, calculated fields, and capture workflow |
| `Honua.Mobile.Offline` | GeoPackage storage, sync queue/engine, map area download, conflict resolution |
| `Honua.Mobile.Maui` | MAUI service registration, DI extensions, native display boundaries, device location orchestration |

## Detailed API Reference

> **TODO:** The full per-member API reference is generated from XML
> documentation comments on the public surface of each package. Until the
> docfx output is published, browse the source directly:
>
> - [`src/Honua.Mobile.Sdk/HonuaMobileClient.cs`](../../src/Honua.Mobile.Sdk/HonuaMobileClient.cs)
> - [`src/Honua.Mobile.Sdk/HonuaMobileClientOptions.cs`](../../src/Honua.Mobile.Sdk/HonuaMobileClientOptions.cs)
> - [`src/Honua.Mobile.Maui/HonuaMobileServiceCollectionExtensions.cs`](../../src/Honua.Mobile.Maui/HonuaMobileServiceCollectionExtensions.cs)
>
> Public types carry `///` XML documentation; IDEs such as Visual Studio and
> Rider surface this inline.

## Related Guides

- [Offline Sync](../guides/offline-sync.md) -- GeoPackage storage, sync engine
  registration, and conflict resolution.
- [Security](../guides/security.md) -- authentication, transport security, and
  secure storage.
- [Native Display and Location](../guides/native-display-and-location.md) --
  display adapter and device-location lifecycle.
