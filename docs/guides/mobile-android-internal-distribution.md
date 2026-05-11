# Mobile Android Internal Distribution

Issue #86 adds a manually triggered GitHub Actions path for signed Android
internal builds. The workflow is intentionally separate from production release
promotion: it only uploads to Google Play Internal App Sharing or the internal
testing track.

## Workflow

Run **Android Internal Distribution** from the GitHub Actions tab.

Inputs:

- `environment`: protected GitHub Environment that gates signing and upload.
  The default is `android-internal`.
- `channel`: `internal-testing` for the Play internal testing track, or
  `internal-app-sharing` for a direct Internal App Sharing install link.
- `artifact_type`: `aab` or `apk`.
- `app_target`: known Android app target to publish. The workflow maps this
  to a MAUI project, Google Play package name, and per-app signing secrets.
  The default `field-collection` target builds
  `apps/Honua.Mobile.FieldCollection/Honua.Mobile.FieldCollection.csproj`
  with package name `io.honua.mobile.fieldcollection`.
- `version_name`: tester-facing application display version.
- `version_code`: Android application version code. Leave blank to use the
  GitHub run number. For Play track uploads, this must be higher than any
  version code already active for the package.
- `release_notes`: short tester-facing release summary.

The workflow checks out the exact workflow-dispatch commit, resolves the
selected app target, installs the Android MAUI workload, validates the project
`ApplicationId` against the expected Google Play package name, publishes a
signed release artifact, calculates a SHA-256 digest, stores the artifact on the
workflow run, uploads to the selected internal Play channel, and writes a run
summary with the version, commit SHA, environment, package name, artifact name,
and digest. It does not accept a separate ref override because signing and Play
upload secrets must only run against the reviewed workflow ref approved for the
protected environment.

Known Android app targets:

| `app_target` | Project | Package name | Signing secret stem |
| --- | --- | --- | --- |
| `field-collection` | `apps/Honua.Mobile.FieldCollection/Honua.Mobile.FieldCollection.csproj` | `io.honua.mobile.fieldcollection` | `ANDROID_FIELD_COLLECTION_UPLOAD` |
| `mobile-app` | `apps/Honua.Mobile.App/Honua.Mobile.App.csproj` | `io.honua.mobile.app` | `ANDROID_APP_UPLOAD` |

The non-secret source of truth for this table is
`quality/android-store-prereqs.json`. Run
`scripts/validate-android-store-prereqs.sh` after changing Android app targets,
package IDs, or signing secret names.

## Required Environment

Create a GitHub Environment named `android-internal` or pass another protected
environment name to the workflow. Configure required reviewers for that
environment before adding upload credentials.

Required environment secrets:

- `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON`: full Google Play service account JSON.
- `<SIGNING_SECRET_STEM>_KEYSTORE_BASE64`: base64-encoded Android
  signing keystore for the selected app target.
- `<SIGNING_SECRET_STEM>_KEYSTORE_PASSWORD`: keystore password.
- `<SIGNING_SECRET_STEM>_KEY_ALIAS`: signing key alias.
- `<SIGNING_SECRET_STEM>_KEY_PASSWORD`: signing key password.

For the default `field-collection` target, configure these signing secrets:

- `ANDROID_FIELD_COLLECTION_UPLOAD_KEYSTORE_BASE64`
- `ANDROID_FIELD_COLLECTION_UPLOAD_KEYSTORE_PASSWORD`
- `ANDROID_FIELD_COLLECTION_UPLOAD_KEY_ALIAS`
- `ANDROID_FIELD_COLLECTION_UPLOAD_KEY_PASSWORD`

For the `mobile-app` target, configure these signing secrets:

- `ANDROID_APP_UPLOAD_KEYSTORE_BASE64`
- `ANDROID_APP_UPLOAD_KEYSTORE_PASSWORD`
- `ANDROID_APP_UPLOAD_KEY_ALIAS`
- `ANDROID_APP_UPLOAD_KEY_PASSWORD`

These names match `docs/guides/mobile-store-prereqs.md` and the non-secret
register in `quality/android-store-prereqs.json`.

Do not configure a free-form Android package-name secret for this workflow. The
package name is derived from `app_target`, and the workflow fails before restore,
signing, or upload if the selected project's `ApplicationId` differs from that
expected package name.

Optional environment variable:

- `GOOGLE_PLAY_TESTER_LINK`: tester opt-in or install instructions URL for the
  internal testing track. Internal App Sharing uploads emit a direct link when
  Google Play returns one.

The Google Play service account needs access to the target app in Play Console
and permission to create internal testing releases or Internal App Sharing
artifacts.

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

To rebuild the same source, rerun the workflow from the same workflow ref and
commit with a new `version_code`. To roll testers back, select a prior internal
build in Play Console when it is still available, or rerun the workflow from the
prior reviewed workflow ref with a higher version code. Tell testers which
version name, version code, commit SHA, and artifact SHA-256 are expected before
they retest.

When an uploaded build is bad, stop assigning it to tester groups in Play
Console or replace it with a newer internal release. Do not promote the internal
track build to production from this workflow.
