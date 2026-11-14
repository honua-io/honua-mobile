# Demo Build Runbook — How to Cut a Customer Demo Build

This runbook builds the SDK reference app against the demo backend at
`https://demo.honua.io`. It verifies packaging and endpoint metadata. The
reference app does not implement the retired field app's login/edit/sync
journey. Collect owns field workflow builds and their on-device test scripts.

Tracks issue
[#292](https://github.com/honua-io/honua-mobile/issues/292). The repo-side build
config and workflow wiring are implemented; the Apple Developer account + external
TestFlight channel and the Android customer-access track remain owner-provided
(see [Owner prerequisites](#owner-prerequisites-not-in-this-repo)).

## What "Demo" means in the build

The demo backend base URL is selected at build time by the
**`HonuaMobileBuildEnvironment`** MSBuild property, the same mechanism the dev /
staging / production builds use. The mapping lives in
[`build/Honua.Mobile.BuildMetadata.props`](../build/Honua.Mobile.BuildMetadata.props):

| `HonuaMobileBuildEnvironment` | Backend baked in | Environment kind |
| --- | --- | --- |
| `demo`, `ios-demo`, `android-demo` | `https://demo.honua.io` | `Demo` (non-production) |

When the build environment is one of the demo names and no explicit
`HonuaMobileApiBaseUrl` is supplied, the props default the base URL to
`https://demo.honua.io`. The value is stamped into assembly metadata. The retired field app's runtime
configuration and endpoint guard no longer exist. Host applications must read
and enforce their endpoint configuration; verify the selected endpoint on-device.
This reference app workflow does not deliver the Collect field workflow.

**No secrets or license live in source.** Only the public base URL is baked in.
The demo backend is Pro-licensed independently (server side); any credentials a
build needs are supplied from the protected GitHub Environment at build time,
exactly like the existing TestFlight / internal-distribution lanes. To point a
demo build at a different host (e.g. a staging demo), pass
`-p:HonuaMobileApiBaseUrl=https://...` — it must be HTTPS and must not be a
production host.

## Pro-backend dependency

A demo build is only useful if `demo.honua.io` is **Pro-licensed and reachable**.
If the demo backend is on the Community edition, editing/sync will fail on-device
with:

```
gRPC FailedPrecondition: Feature Editing requires an active Pro entitlement.
Current edition is Community; install a license that includes 'editing.feature-edits'.
```

Confirm the demo server is up and Pro-licensed **before** inviting a customer.
The license is issued and applied server-side (honua-server demo environment) and
is not part of this repo.

## Cutting an iOS demo build (TestFlight)

Trigger [`ios-testflight.yml`](../.github/workflows/ios-testflight.yml) via
**Actions → iOS TestFlight → Run workflow**:

| Input | Value for a demo build |
| --- | --- |
| `target_environment` | `ios-demo` |
| `app_target` | `mobile-app` |
| `build_number` | optional; defaults to the run number |
| `release_notes` | optional tester notes |

`ios-demo` names the protected GitHub Environment that holds the Apple signing
and App Store Connect secrets, **and** is passed through as
`HonuaMobileBuildEnvironment`, so the IPA is stamped with `https://demo.honua.io`.
Signing, bundle-ID validation, and the App Store Connect upload are unchanged from
the standard TestFlight lane.

## Cutting an Android demo build (Play internal / Internal App Sharing)

Trigger
[`android-internal-distribution.yml`](../.github/workflows/android-internal-distribution.yml)
via **Actions → Android Internal Distribution → Run workflow**:

| Input | Value for a demo build |
| --- | --- |
| `environment` | the protected Environment holding signing + Play secrets (e.g. `android-internal`) |
| `target_environment` | `android-demo` |
| `app_target` | `mobile-app` |
| `channel` | `internal-testing` (durable) or `internal-app-sharing` (per-build link) |
| `artifact_type` | `aab` for Play tracks, `apk` for sideload |
| `version_name` / `version_code` | as usual; `version_code` must increase for Play uploads |

`environment` still selects the protected signing/upload secrets;
`target_environment=android-demo` is passed through as
`HonuaMobileBuildEnvironment`, so the artifact is stamped with
`https://demo.honua.io`.

## On-device smoke checklist

Run this on the installed demo build before handing it to a customer. It proves
the Pro `editing.feature-edits` path is green against `demo.honua.io`.

The former field app's login/edit/sync journey has moved to Collect. Use the
[paired Collect build script](guides/field-sdk-consolidation.md#paired-build-test-script)
with the intended demo endpoint. Record the installed build, endpoint, login,
offline capture, restart and successful reconnect/sync results on both phones.
A reference-app store upload alone does not validate that journey.

If sync fails with the `FailedPrecondition` message above, the demo backend is
not Pro-licensed — fix the server license, not the build.

## What the external channel expects

- **iOS / TestFlight:** an external tester group with a public install link, with
  Beta App Review submitted/approved, so non-org customers can install via the
  link. This requires an Apple Developer account and is owner-provided.
- **Android:** either a closed-testing track (durable tester list) or an Internal
  App Sharing link (per-build, ephemeral). Internal App Sharing is the fastest for
  a one-off demo; closed testing is better for a standing demo audience.

## Owner prerequisites (not in this repo)

These remain owner work and are intentionally **not** implemented here because they
require an Apple Developer account, signing secrets, or store-console
configuration:

- Apple Developer account, distribution certificate, App Store Connect API key,
  and the `ios-demo` provisioning profile loaded into the protected `ios-demo`
  GitHub Environment.
- External TestFlight tester group + public link + Beta App Review submission.
- Android closed-testing track or Internal App Sharing customer-access path and the
  associated Play upload secrets in the protected environment.
- Pro license issuance / activation on the `demo.honua.io` server.
