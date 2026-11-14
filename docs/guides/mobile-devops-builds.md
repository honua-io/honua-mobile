# Mobile DevOps Builds

The maintained MAUI reference app is `apps/Honua.Mobile.App`. Use the
[Android internal distribution](mobile-android-internal-distribution.md) and
[iOS TestFlight](mobile-testflight-builds.md) workflows with `app_target=mobile-app`.
Their run summaries record the source commit, version, artifact and digest.
The duplicate field app and its dedicated debug APK workflow have been retired.

Collect owns its installable field app builds. The
[field SDK consolidation journey](field-sdk-consolidation.md#paired-build-test-script)
records paired SDK/package versions and physical Android/iOS results. A reference
app build or simulator result does not satisfy that phone journey.

## PR Platform Smoke

The PR CI MAUI Android and iOS jobs include a device-hosted live query smoke for
honua-server #827 and #828. The smoke app lives at
`tests/Honua.Mobile.PlatformSmoke/Honua.Mobile.PlatformSmoke.csproj`, installs
onto the active emulator or simulator, and queries through the existing
`HonuaMobileSdkFeatureClient` as `IHonuaFeatureQueryClient`. It does not add a
new server API client or duplicate SDK feature contracts.

Required GitHub repository variables:

- `HONUA_MOBILE_SMOKE_BASE_URL`: public Honua server base URL.
- `HONUA_MOBILE_SMOKE_SERVICE_ID`: public feature service ID.
- `HONUA_MOBILE_SMOKE_LAYER_ID`: public feature layer ID.

Optional GitHub Actions secret:

- `HONUA_MOBILE_SMOKE_API_KEY`: API key when the selected smoke layer is not
  anonymous.

The app writes `honua-mobile-platform-smoke-result.json` in its mobile app data
sandbox. CI fails if the app does not produce the marker, the query fails, or
the live feature query takes one second or longer.

Exact Android command, after an emulator is booted and `adb` is available:

```bash
HONUA_MOBILE_SMOKE_BASE_URL="https://example.honua.server/" \
HONUA_MOBILE_SMOKE_SERVICE_ID="mobile_offline_demo" \
HONUA_MOBILE_SMOKE_LAYER_ID="68910" \
scripts/run-android-platform-smoke.sh
```

Exact iOS command, on macOS after the iOS MAUI workload and Xcode are selected:

```bash
HONUA_MOBILE_SMOKE_BASE_URL="https://example.honua.server/" \
HONUA_MOBILE_SMOKE_SERVICE_ID="mobile_offline_demo" \
HONUA_MOBILE_SMOKE_LAYER_ID="68910" \
scripts/run-ios-platform-smoke.sh
```

Set `HONUA_MOBILE_SMOKE_API_KEY` in the shell for either command if the layer
requires it. Set `IOS_SIMULATOR_UDID` to force a specific booted simulator; when
unset, the iOS script uses a booted simulator or creates an available iPhone
simulator. Set `RUNTIME_IDENTIFIER=iossimulator-arm64` on Apple Silicon runners
when the selected .NET iOS workload and simulator require it.

The broader live Honua server integration suite remains in
`tests/Honua.Mobile.ServerIntegration.Tests` and is enabled with
`HONUA_MOBILE_LIVE_SERVER_TESTS=true`. Use that Testcontainers-backed harness
for server interaction coverage beyond the one-query emulator/simulator smoke.

## Build traceability

Record the workflow URL, exact commit, package versions, application version,
platform and artifact digest with each device test. Signing assets and store
access remain in protected environments; see [store prerequisites](mobile-store-prereqs.md).
Choose and verify the intended test endpoint in the installed application before
capturing data. The retired field app's runtime endpoint guard is no longer a
reference-app capability.
