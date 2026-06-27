# AR Utility Visualization (Concept)

> **Status: forward-looking concept — not implemented.**
> This document describes a *proposed* augmented-reality experience for visualizing
> underground utilities on top of Honua feature data. There is **no application
> code, no SDK, and no installable package** behind it yet. Everything below is a
> design sketch for discussion, not shipped functionality. Do not cite it as an
> available capability.

## Idea

Overlay digital utility data (water, gas, electric, telecom, sewer) onto a live
camera feed so field workers can see the approximate position and depth of
underground infrastructure before excavating. The concept would build on Honua's
existing feature-query and offline capabilities.

## Why it is a concept and not an example

The earlier version of this file advertised a complete, shippable application —
with install steps, a `@honua/react-native-sdk` import, and external links — none
of which exist. To avoid misleading readers, the marketing content and fabricated
code samples were removed. This file is intentionally short until a real prototype
lands.

## What would be required to build it

- A React Native (or MAUI) host app with ARKit (iOS) / ARCore (Android) integration.
- A published mobile SDK surface for AR rendering (does not exist today).
- Spatial queries against a Honua FeatureServer to load utilities near the device,
  using the gRPC-first transport already provided by `Honua.Mobile.Sdk`.
- A coordinate-alignment step mapping WGS84 GPS positions into local AR space.

## Open questions

- Positional accuracy: AR anchoring on consumer GPS is typically only metre-level;
  utility-strike prevention needs survey-grade corrections (RTK) to be safe.
- Occlusion and depth rendering fidelity across device tiers.
- Offline caching and data-freshness guarantees for safety-critical use.

## Contributing

If you want to prototype this, start a discussion in the repository's issues
before writing code, so the SDK surface and accuracy requirements can be agreed
up front.

## License

This repository is licensed under the [Apache License 2.0](../../LICENSE).
