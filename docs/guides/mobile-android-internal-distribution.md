# Mobile Android Internal Distribution

Issue #86 adds a manually triggered GitHub Actions path for signed Android
internal builds. The workflow is intentionally separate from production release
promotion: it only uploads to Google Play Internal App Sharing or the internal
testing track.

## Workflow

Run **Android Internal Distribution** from the GitHub Actions tab.

Inputs:

- `source_ref`: branch, tag, or commit SHA to build. Leave blank to use the
  workflow branch selected in the Actions UI.
- `environment`: protected GitHub Environment that gates signing and upload.
  The default is `android-internal`.
- `channel`: `internal-testing` for the Play internal testing track, or
  `internal-app-sharing` for a direct Internal App Sharing install link.
- `artifact_type`: `aab` or `apk`.
- `app_project`: MAUI app project to publish. The default builds
  `apps/Honua.Mobile.FieldCollection/Honua.Mobile.FieldCollection.csproj`.
- `version_name`: tester-facing application display version.
- `version_code`: Android application version code. Leave blank to use the
  GitHub run number. For Play track uploads, this must be higher than any
  version code already active for the package.
- `release_notes`: short tester-facing release summary.

The workflow checks out the selected ref, installs the Android MAUI workload,
validates the selected app project against the protected-environment secrets,
publishes a signed release artifact, calculates a SHA-256 digest, stores the
artifact on the workflow run, uploads to the selected internal Play channel, and
writes a run summary with the version, commit SHA, environment, artifact name,
and digest.

The validation step runs `scripts/validate-android-store-prereqs.sh` before any
build or upload work. It fails if required secrets are missing, the Google Play
service account JSON is malformed, the keystore secret is not valid base64, or
`ANDROID_PACKAGE_NAME` does not match the selected project's `<ApplicationId>`.

## Required Environment

Create a GitHub Environment named `android-internal` or pass another protected
environment name to the workflow. Configure required reviewers for that
environment before adding upload credentials.

Required environment secrets:

- `ANDROID_KEYSTORE_BASE64`: base64-encoded Android signing keystore.
- `ANDROID_KEYSTORE_PASSWORD`: keystore password.
- `ANDROID_KEY_ALIAS`: signing key alias.
- `ANDROID_KEY_PASSWORD`: signing key password.
- `ANDROID_PACKAGE_NAME`: Google Play package name, for example
  `io.honua.mobile.fieldcollection`.
- `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON`: full Google Play service account JSON.

Optional environment variable:

- `GOOGLE_PLAY_TESTER_LINK`: tester opt-in or install instructions URL for the
  internal testing track. Internal App Sharing uploads emit a direct link when
  Google Play returns one.

The Google Play service account needs access to the target app in Play Console
and permission to create internal testing releases or Internal App Sharing
artifacts.

For the default Field Collection project, `ANDROID_PACKAGE_NAME` must be
`io.honua.mobile.fieldcollection`. For `apps/Honua.Mobile.App`, it must be
`io.honua.mobile.app`. Use separate protected environments for each app when
the signing material differs.

## Preflight

Run **Android Store Preflight** before the first upload, after any signing
secret rotation, and before production promotion. The preflight workflow uses
the same protected environment and validation script as the upload workflow but
does not build or upload an artifact.

The preflight proves the repo-visible parts of issue #85:

- the selected MAUI project has a final `<ApplicationId>`;
- `ANDROID_PACKAGE_NAME` matches that application ID;
- required Android signing and Play upload secrets are available through the
  selected protected environment;
- the Play service account JSON has the expected service-account fields;
- the keystore secret decodes to a non-empty file.

## Signing Setup

Encode the keystore before saving it as a secret:

```bash
base64 -w 0 honua-internal.keystore
```

Use a non-production signing key unless release management explicitly decides
that internal builds must match the production app signing lineage. Keep the
keystore and Google Play JSON only in the protected GitHub Environment, not as
repository-wide secrets.

## Tester Install

For `internal-app-sharing`, testers use the link in the workflow summary. They
must be allowed to use Internal App Sharing for the Play account on their device.

For `internal-testing`, testers install from the app's internal testing track in
Google Play. Add tester groups in Play Console, set `GOOGLE_PLAY_TESTER_LINK`
when an opt-in URL is available, and direct testers to the run summary for the
version, commit, and digest they should validate.

## Rollback and Rebuild

Internal distribution does not touch production tracks.

To rebuild the same source, rerun the workflow with the same `source_ref` and a
new `version_code`. To roll testers back, select a prior internal build in Play
Console when it is still available, or rerun the workflow from the prior commit
with a higher version code. Tell testers which version name, version code,
commit SHA, and artifact SHA-256 are expected before they retest.

When an uploaded build is bad, stop assigning it to tester groups in Play
Console or replace it with a newer internal release. Do not promote the internal
track build to production from this workflow.
