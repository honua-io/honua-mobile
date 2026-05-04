# Mobile TestFlight Builds

Issue #88 adds a manual GitHub Actions workflow for signed iPhone builds. Use
`.github/workflows/ios-testflight.yml` when a developer needs a TestFlight build
without a local Mac.

The workflow builds `net10.0-ios`, signs an `ios-arm64` IPA with protected Apple
signing assets, uploads the IPA to App Store Connect, and writes a run summary
with the version, commit SHA, upload status, release notes, and tester
instructions.

## Required Setup

Create or verify a protected GitHub Environment before the first upload. The
default environment is `ios-testflight`. Require approval from the release
manager or Apple owner so the job cannot access signing and upload secrets until
the environment deployment is approved.

Store these secrets in the protected environment, not as repository-wide
secrets:

- `APPLE_TEAM_ID`
  Apple Developer team ID.
- `APP_STORE_CONNECT_ISSUER_ID`
  App Store Connect API issuer ID.
- `APP_STORE_CONNECT_KEY_ID`
  App Store Connect API key ID.
- `APP_STORE_CONNECT_API_KEY_P8`
  Full private `.p8` API key contents.
- `IOS_DISTRIBUTION_CERTIFICATE_P12_BASE64`
  Base64-encoded Apple Distribution certificate export.
- `IOS_DISTRIBUTION_CERTIFICATE_PASSWORD`
  Password for the `.p12` export.
- `IOS_APP_PROVISIONING_PROFILE_MOBILEPROVISION_BASE64`
  Base64-encoded App Store provisioning profile for
  `io.honua.mobile.app`.
- `IOS_FIELD_COLLECTION_PROVISIONING_PROFILE_MOBILEPROVISION_BASE64`
  Base64-encoded App Store provisioning profile for
  `io.honua.mobile.fieldcollection`.

Encode binary files without line wrapping:

```bash
base64 -i ios-distribution.p12 | pbcopy
base64 -i Honua_FieldCollection_AppStore.mobileprovision | pbcopy
```

The App Store Connect API key needs permission to upload builds for the app
record. Keep the original `.p8`, `.p12`, and `.mobileprovision` files in the
approved secrets vault outside this repository.

## Running a Build

1. Open GitHub Actions and select `iOS TestFlight`.
2. Select the workflow branch.
3. Fill the dispatch inputs:
   - `source_ref`: branch, tag, or commit SHA to check out. Leave blank to use
     the selected workflow ref.
   - `target_environment`: normally `ios-testflight`.
   - `app_target`: `field-collection` or `mobile-app`.
   - `build_number`: optional numeric iOS build number. Leave blank to use the
     GitHub run number.
   - `release_notes`: optional notes copied into the run summary and artifact.
4. Start the run and wait for the protected environment approval.
5. After upload, wait for App Store Connect processing to finish before asking
   testers to install.

The workflow uploads a signed artifact containing:

- The signed `.ipa`.
- The `.xcarchive` zip when the .NET publish output includes one.
- JSON build metadata.
- Tester notes.

## Tester Instructions

Internal testers must be invited to the App Store Connect app and have the
TestFlight app installed on their iPhone.

After App Store Connect finishes processing:

1. Open TestFlight.
2. Select the Honua app that matches the workflow summary.
3. Install the version and build number shown in the summary.
4. Confirm the tested build matches the summary commit SHA.
5. Report installation or runtime issues with the GitHub Actions run link.

Do not put tester email addresses in workflow files or committed docs. Manage
tester membership in App Store Connect.

## Runner and Xcode Notes

The workflow uses `macos-latest`, pins .NET SDK `10.0.100`, installs the iOS
workload with `--skip-manifest-update`, and explicitly selects Xcode 26.3 from
`/Applications/Xcode_26.3.app`. Keep those values aligned because newer .NET
iOS workload manifests can require newer Xcode toolchains than the selected
hosted runner provides.

When moving to a newer Microsoft iOS workload, update `DOTNET_VERSION`,
`REQUIRED_XCODE_VERSION`, and the runner image together, then verify that the
hosted image includes the complete Xcode toolchain before TestFlight uploads
are enabled.

## Troubleshooting

Missing protected environment approval:

- Confirm the workflow run is waiting on the selected GitHub Environment.
- Confirm the release manager or Apple owner is listed as an environment
  reviewer.

Missing secret error:

- Add the named secret to the selected environment.
- Do not move signing or App Store Connect secrets to repository scope.

Provisioning profile does not match bundle ID:

- Use `IOS_APP_PROVISIONING_PROFILE_MOBILEPROVISION_BASE64` for
  `io.honua.mobile.app`.
- Use `IOS_FIELD_COLLECTION_PROVISIONING_PROFILE_MOBILEPROVISION_BASE64` for
  `io.honua.mobile.fieldcollection`.
- Regenerate the App Store profile after changing bundle ID capabilities.

No Apple Distribution identity found:

- Export an Apple Distribution certificate as `.p12`.
- Confirm the certificate has its private key before exporting.
- Replace both the base64 certificate secret and its password.

No IPA produced:

- Inspect the `Publish signed iOS IPA` step first.
- Confirm the project still targets `net10.0-ios`.
- Confirm the publish command includes `RuntimeIdentifier=ios-arm64`,
  `ArchiveOnBuild=true`, and `BuildIpa=true`.

App Store Connect upload failed:

- Confirm the API key is active and has access to the selected app record.
- Confirm the bundle ID has an App Store Connect app record.
- Confirm the build number has not already been uploaded for the same version.
- Use the `xcrun altool` error in the upload step; the workflow does not echo
  API key, certificate, or provisioning profile values.

Build is uploaded but not visible in TestFlight:

- Wait for App Store Connect processing.
- Check App Store Connect for compliance or missing export status prompts.
- Confirm the build is assigned to the internal tester group.
