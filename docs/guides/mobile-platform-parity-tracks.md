# Mobile Platform Parity Tracks

Last reviewed: 2026-05-04.

Issue #22 owns the beta follow-on plan for expanding beyond the single-path
MAUI mobile slice into Flutter and native iOS/Android tracks. This page defines
the parity target without adding SDK-neutral clients, contracts, DTOs, geometry
logic, or copied SDK source to `honua-mobile`.

## Scope Boundaries

`honua-mobile` owns platform runtime integration:

- MAUI, native Android, native iOS, and Flutter app bootstrap guidance.
- Native storage, secure storage, media, permissions, location, background
  scheduling, WebView/browser cache, and display adapter behavior.
- Platform templates, example app wiring, device smoke criteria, and release
  lane documentation.
- Thin adapters that translate published `Honua.Sdk.*` contracts into runtime
  behavior for each mobile host.

`honua-mobile` does not own:

- Provider-neutral feature, edit, routing, geocoding, catalog, scene, field,
  stream, or replica sync clients.
- Stable server DTOs, field schemas, validation engines, plugin manifests,
  geometry primitives, CRS transforms, or spatial indexes.
- Long-lived sibling `ProjectReference` links to `honua-sdk-dotnet` or copied
  SDK source.

When a parity item needs portable contracts, create or link the
`honua-sdk-dotnet` issue first and keep this repo limited to adapter,
registration, template, and platform verification work.

## Track Roles

| Track | Role | First useful artifact | Non-goal |
|-------|------|-----------------------|----------|
| MAUI reference | Existing reference implementation for field collection, offline storage, auth, display, and device integration. | Hardened template and smoke checklist that other tracks can mirror. | Reopening SDK-neutral model ownership inside `Honua.Mobile.Sdk`. |
| Native Android | Kotlin/Jetpack integration track for Android-specific storage, permissions, lifecycle, maps/WebView, media, and background work. | Minimal Android sample app plus adapter checklist over versioned SDK packages or SDK-owned generated bindings. | Defining Android-only server clients that drift from SDK contracts. |
| Native iOS | Swift/SwiftUI integration track for iOS storage, Keychain, background tasks, MapKit/WKWebView, media, and location behavior. | Minimal iOS sample app plus adapter checklist over versioned SDK packages or SDK-owned generated bindings. | Creating independent Swift DTOs for shared server contracts. |
| Flutter | Flutter plugin and sample track that presents a Dart-friendly surface while delegating platform work to Android/iOS adapters. | Flutter package skeleton, example app, and channel contract for runtime-only operations. | Reimplementing Honua API clients in Dart. |

## Platform Parity Matrix

Status values describe the intended beta target, not current production support.

| Capability | Priority | MAUI reference | Native Android target | Native iOS target | Flutter target | Contract owner |
|------------|----------|----------------|-----------------------|-------------------|----------------|----------------|
| App bootstrap and dependency registration | P0 | `MauiProgram` and template registration. | Android `Application`/Activity setup for adapters and lifecycle. | `UIApplicationDelegate` or SwiftUI app setup for adapters and lifecycle. | Plugin registrar initializes Android/iOS adapters. | Mobile |
| Auth token persistence | P0 | MAUI secure storage adapter. | Android Keystore-backed storage adapter. | iOS Keychain storage adapter. | Dart API delegates to platform secure storage. | Mobile adapter over SDK auth abstractions |
| Offline file placement and cleanup | P0 | App data/cache directories for GeoPackage and package-local files. | App-specific files, cache, and no-backup directory policy. | Application Support, Caches, and backup-exclusion policy. | Plugin delegates file roots and cleanup to native adapters. | Mobile adapter over SDK offline contracts |
| Field form rendering and validation display | P0 | MAUI field screens and validation presentation. | Native form renderer consumes SDK field schema/validation results. | SwiftUI/UIKit renderer consumes SDK field schema/validation results. | Flutter widgets consume SDK-sourced schema/validation results through adapters. | SDK owns schema/validation; mobile owns presentation |
| Media capture and local paths | P0 | MAUI media capture and local media path handling. | CameraX/photo picker integration with mobile-owned path policy. | AVFoundation/photo picker integration with mobile-owned path policy. | Flutter plugin delegates capture and path policy to native adapters. | Mobile |
| GPS and reachability | P0 | MAUI Essentials integration and lifecycle handling. | Android location provider, permission state, and connectivity callbacks. | Core Location, permission state, and reachability callbacks. | Dart streams mirror native permission and lifecycle states. | Mobile consumes SDK geofence/event contracts when available |
| Background sync scheduling | P1 | Mobile runtime orchestrator and app lifecycle hooks. | WorkManager or foreground-service policy where product-approved. | BGTaskScheduler/background fetch policy where product-approved. | Plugin exposes status/control and delegates scheduling to native adapters. | Mobile adapter over SDK sync contracts |
| Map and annotation display | P1 | MAUI/WebView/native display adapter paths. | Android map or WebView host consuming SDK feature descriptors. | MapKit/WKWebView host consuming SDK feature descriptors. | Flutter `PlatformView` or texture host around Android/iOS display adapters. | SDK owns descriptors; mobile owns render host |
| 3D scene preview | P1 | WebView-hosted `<honua-scene>` integration. | Android WebView scene host with cache/auth boundaries. | WKWebView scene host with cache/auth boundaries. | Flutter WebView/PlatformView host after native behavior is stable. | SDK/server own scene contracts; mobile owns host/cache integration |
| Native AR anchoring | P2 | MAUI native handler direction after anchoring decisions. | ARCore handler track after native anchoring scope is approved. | ARKit handler track after native anchoring scope is approved. | No first-class Flutter AR track until Android/iOS anchors stabilize. | Mobile runtime over SDK/server scene dependencies |
| Diagnostics and release metadata | P1 | Template/release metadata and mobile diagnostics surface. | Logcat/crash/reporting handoff plus artifact metadata. | Unified logging/crash/reporting handoff plus artifact metadata. | Dart-facing diagnostics mirror native adapter state. | Mobile/release |

## Priority Client Feature Map

P0 parity is the minimum beta bar for any new platform track:

| Feature group | Android native | iOS native | Flutter |
|---------------|----------------|------------|---------|
| Project/app bootstrap | Native sample app registers auth, storage, display, media, and location adapters. | Native sample app registers auth, storage, display, media, and location adapters. | Example app registers the plugin and proves Android/iOS adapter initialization. |
| Auth and secure storage | Token save/load/delete tests cover Android Keystore behavior. | Token save/load/delete tests cover Keychain behavior. | Widget/integration test covers token API delegation without storing secrets in Dart files. |
| Offline workspace | Device smoke creates, reopens, evicts, and reports a GeoPackage/package directory. | Device smoke creates, reopens, evicts, and reports a GeoPackage/package directory. | Plugin smoke reports platform file roots and cleanup status. |
| Field capture | Camera, gallery, local media path, and validation presentation work in the sample. | Camera, photo library, local media path, and validation presentation work in the sample. | Flutter UI delegates capture to native adapters and displays returned local paths. |
| Map/display host | Sample renders SDK-sourced features through the chosen Android map/WebView host. | Sample renders SDK-sourced features through the chosen iOS map/WebView host. | Flutter view embeds the native host and mirrors camera/selection events. |
| Runtime state | Permission, reachability, battery, and sync state are observable for support. | Permission, reachability, battery, and sync state are observable for support. | Dart stream mirrors normalized native runtime state. |

P1 expands beta hardening after P0 device smoke is stable:

| Feature group | Android native | iOS native | Flutter |
|---------------|----------------|------------|---------|
| Background sync | WorkManager/foreground-service policy documented and tested against battery restrictions. | BGTaskScheduler/background fetch policy documented and tested against iOS limits. | Exposes control/status only; scheduling remains native. |
| 3D scene preview | WebView host validates protected scene auth, cache eviction, and offline package paths. | WKWebView host validates protected scene auth, cache eviction, and offline package paths. | Embeds native scene host once Android/iOS WebView behavior is stable. |
| Release diagnostics | Internal artifact includes platform metadata and support notes. | TestFlight/internal artifact includes platform metadata and support notes. | Example build records plugin/native adapter versions. |

P2 should wait for approved dependencies:

| Feature group | Android native | iOS native | Flutter |
|---------------|----------------|------------|---------|
| Native AR | Starts after ARCore/ARKit anchoring requirements and scene dependencies are accepted. | Starts after ARCore/ARKit anchoring requirements and scene dependencies are accepted. | Deferred until native AR tracks produce stable confidence and lifecycle states. |
| Plugin host extensions | Runtime host only after SDK/server plugin contracts exist. | Runtime host only after SDK/server plugin contracts exist. | Flutter presentation only after shared plugin manifest ownership is settled outside mobile. |

## Build and Release Requirements

This page records requirements only. Workflow implementation stays in focused
follow-up PRs so platform parity does not collide with active CI, store, or
diagnostics work.

| Platform track | CI requirement | Release lane requirement | Required verification |
|----------------|----------------|--------------------------|-----------------------|
| MAUI Android | `dotnet test` plus Android MAUI workload build for the reference app/template. | Debug artifact lane for sideloading; signed internal distribution lane for beta testers. | Emulator smoke, physical Android smoke, trim/AOT warning review, install notes, artifact metadata. |
| MAUI iOS | macOS runner with pinned .NET/Xcode inputs and simulator build where signing is not available. | TestFlight or internal ad hoc lane with explicit signing secrets and release-owner approval. | Simulator build, physical iOS smoke, entitlement review, TestFlight notes, artifact metadata. |
| Native Android | Gradle/Kotlin build, unit tests, lint, and instrumentation smoke for adapter sample. | Signed APK/AAB internal lane using Play or enterprise distribution credentials. | Android API spread, permission prompts, background limits, storage cleanup, map/WebView smoke. |
| Native iOS | Xcode build, Swift tests, static analysis, and simulator smoke for adapter sample. | TestFlight/ad hoc lane with provisioning profile, certificates, and export options. | iOS version spread, permissions, background task limits, backup exclusion, WKWebView/map smoke. |
| Flutter | `flutter analyze`, `flutter test`, Android example build, and iOS example build on macOS. | Android/iOS release relies on native signing lanes; Flutter package publishing requires separate approval. | Plugin channel tests, example app smoke, adapter version reporting, platform fallback behavior. |

## Phased Rollout Plan

| Phase | Exit criteria | Approval checkpoint |
|-------|---------------|---------------------|
| 0. Planning acceptance | This roadmap, parity matrix, priority feature map, and build/release requirements are reviewed on #22. | Product, mobile, SDK, and release owners acknowledge the matrix before platform implementation starts. |
| 1. MAUI reference lock | Reference app/template documents P0 behavior and has smoke coverage that other tracks can compare against. | Mobile owner confirms the reference behavior is the parity baseline. |
| 2. Native Android preview | Android sample wires P0 adapters, passes local/device smoke, and consumes only approved SDK packages or SDK-owned generated bindings. | Mobile and SDK owners confirm no Android-local contract drift. |
| 3. Native iOS preview | iOS sample wires P0 adapters, passes simulator/device smoke, and consumes only approved SDK packages or SDK-owned generated bindings. | Mobile and SDK owners confirm no iOS-local contract drift. |
| 4. Flutter preview | Flutter plugin delegates P0 runtime behavior to Android/iOS adapters and passes analyze/test/example smoke. | Mobile owner confirms the Dart API is runtime integration only. |
| 5. Beta parity hardening | P1 diagnostics, background policy, display, and scene preview requirements pass on representative devices. | Product and release owners approve beta tester distribution. |

## Follow-up Ticket Template

Each implementation ticket created from this roadmap should include:

- Platform track and priority group.
- SDK package or external contract dependency, with linked issue when missing.
- Mobile-owned adapter/template/test files expected to change.
- Device or simulator validation matrix.
- Build or release lane impact, if any.
- Explicit statement that no SDK-neutral clients/contracts or copied SDK source
  are being added in `honua-mobile`.
