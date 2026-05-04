# Mobile DevOps Builds

This guide defines the mobile build metadata scheme used by GitHub Actions
phone artifacts and the endpoint rules that keep debug APKs out of production
by default.

## Android Debug APK Workflow

Maintainers can run **Android Debug APK** from GitHub Actions with
`workflow_dispatch`. The workflow builds
`apps/Honua.Mobile.FieldCollection/Honua.Mobile.FieldCollection.csproj` for
`net10.0-android` in `Debug` configuration and uploads a sideloadable APK.

The artifact name is deterministic:

```text
honua-fieldcollection-android-debug-<branch-slug>-<short-sha>-run-<run-number>-attempt-<attempt>
```

Each artifact contains:

- `<artifact-name>.apk`
- `<artifact-name>-install-notes.md`
- `<artifact-name>-metadata.json`

The install notes include the selected non-production endpoint, the source
commit, the run number, and basic sideload/adb instructions. The workflow fails
if no APK is found after the publish step.

## Build Metadata

GitHub Actions mobile artifacts should be traceable without guessing. The debug
APK workflow emits this metadata beside the APK:

| Field | Source |
| --- | --- |
| `repository` | `github.repository` |
| `workflow` | `github.workflow` |
| `run_id` | `github.run_id` |
| `run_number` | `github.run_number` |
| `run_attempt` | `github.run_attempt` |
| `ref_name` | `github.ref_name` |
| `branch_slug` | Sanitized `github.ref_name` for artifact names |
| `sha` | Full commit SHA |
| `short_sha` | First 12 characters of the commit SHA |
| `target_environment` | Manual input: `dev`, `staging`, or `custom-nonprod` |
| `api_base_url` | Required manual HTTPS non-production endpoint input |
| `configuration` | `Debug` |
| `target_framework` | `net10.0-android` |
| `application_display_version` | `0.0.0-debug.<run-number>` |
| `application_version` | `github.run_number` |
| `artifact_name` | Deterministic artifact name |
| `apk_name` | Copied APK filename inside the artifact |

For this CI/docs slice of #84, metadata is attached to the build artifact.
Future in-app diagnostics work can surface the same fields inside the app
without changing the artifact naming contract.

## Version Numbers

Debug phone artifacts use the workflow run number as Android
`ApplicationVersion` and `0.0.0-debug.<run-number>` as
`ApplicationDisplayVersion`. `github.run_number` is monotonic for this workflow,
which is enough for tester APK replacement behavior and audit trails.

Store and beta release lanes should use their own signed workflows and release
version policy. Do not infer production release ordering from the debug APK
version code.

## Endpoint Safety

The debug APK workflow does not offer a production environment option. The
allowed values are `dev`, `staging`, and `custom-nonprod`; the API base URL is a
required HTTPS input.

The workflow refuses to build when endpoint metadata is blank, non-HTTPS, or
matches production-looking Honua hosts such as `api.honua.io`, `honua.io`,
`www.honua.io`, `prod.*`, or `production.*`. That keeps phone artifacts from
quietly defaulting to production metadata.

The current FieldCollection app stores the server URL only after the tester
enters it in the app. Debug artifacts therefore ship with install notes and
metadata that point testers to the selected non-production endpoint, rather
than embedding a production default. Production endpoint selection belongs in a
separate signed release lane with explicit release-owner approval.
