# Disconnected Field Workflow Harness

This runbook covers the mobile-owned acceptance harness for the cloud Honua
disconnected field workflow. It validates the non-UI sequence around mobile
GeoPackage storage, SDK-backed feature edit upload, pending operation handling,
and evidence capture. It does not define new server clients or portable SDK
contracts.

## Scope

The harness lives in
`tests/Honua.Mobile.ServerIntegration.Tests/DisconnectedFieldWorkflowAcceptanceTests.cs`.
It exercises this deterministic sequence:

1. `online-download`: create or reuse a FeatureServer replica and download
   changes into the mobile GeoPackage cache.
2. `offline-edit`: queue planned offline operations while the harness is
   logically disconnected.
3. `reconnect-sync`: run the mobile sync engine over the GeoPackage queue and
   upload pending feature edits through the existing SDK-backed mobile client.
4. `verify`: assert local cache state, pending queue counts, sync cursors, and
   cloud or loopback reconciliation evidence.

The deterministic plan includes:

| Operation ID | Kind | Sync behavior |
| --- | --- | --- |
| `op-acceptance-add-001` | `feature-create` | Queued as `OfflineOperationType.Add` and uploaded through FeatureServer `applyEdits`. |
| `op-acceptance-update-001` | `feature-update` | Queued as `OfflineOperationType.Update` and uploaded through FeatureServer `applyEdits`. |
| `op-acceptance-delete-001` | `feature-delete` | Queued as `OfflineOperationType.Delete` and uploaded through FeatureServer `applyEdits`. |
| `op-acceptance-media-001` | `attachment-metadata` | Recorded as planned media evidence metadata. It is not uploaded until an SDK/server attachment journal contract exists. |

The loopback test runs by default against the in-process integration server and
is suitable for local validation. The cloud/staging test is gated so ordinary
test runs do not mutate a shared environment.

## Cloud Fixture Inputs

Set these variables only for a seeded cloud or staging environment that is safe
for acceptance edits:

| Variable | Required | Description |
| --- | --- | --- |
| `HONUA_MOBILE_CLOUD_ACCEPTANCE` | Yes | Set to `1` or `true` to enable the cloud acceptance path. |
| `HONUA_MOBILE_CLOUD_BASE_URL` | Yes | Base URL for cloud Honua, for example `https://staging-api.honua.io`. |
| `HONUA_MOBILE_CLOUD_SERVICE_ID` | Yes | FeatureServer service id for the seeded fixture. |
| `HONUA_MOBILE_CLOUD_LAYER_IDS` | No | Comma-separated FeatureServer layer ids. Defaults to `0`; the first layer is the editable source. |
| `HONUA_MOBILE_CLOUD_API_KEY` | No | API key sent as `X-API-Key` when the fixture uses API key auth. |
| `HONUA_MOBILE_CLOUD_BEARER_TOKEN` | No | Bearer token when the fixture uses token auth. |
| `HONUA_MOBILE_CLOUD_VERIFY_READBACK` | No | Defaults to `true`. Set to `0`, `false`, or `no` only for bring-up runs where the fixture can accept edits but cannot yet answer readback queries. |
| `HONUA_MOBILE_ACCEPTANCE_PACKAGE_ID` | No | Package id recorded in queued operation payloads and evidence. Defaults to `pkg_acceptance_field_workflow`. |
| `HONUA_MOBILE_ACCEPTANCE_RUN_ID` | No | Stable run id used in operation metadata and artifact file names. Defaults to a UTC timestamped cloud run id. |
| `HONUA_MOBILE_ACCEPTANCE_EVIDENCE_DIR` | No | Directory for evidence artifacts. Defaults to a temp directory for cloud runs. |
| `HONUA_MOBILE_ACCEPTANCE_DATABASE_PATH` | No | GeoPackage path for the cloud run. Defaults under the evidence directory. |

The fixture must support the FeatureServer replica endpoints used by
`ReplicaSyncClient` and the `applyEdits` endpoint used by
`HonuaApiOfflineOperationUploader`:

| Endpoint | Purpose |
| --- | --- |
| `/rest/services/{serviceId}/FeatureServer/createReplica` | Seed the local replica cursor. |
| `/rest/services/{serviceId}/FeatureServer/extractChanges` | Download deterministic pre-edit features into the GeoPackage cache. |
| `/rest/services/{serviceId}/FeatureServer/synchronizeReplica` | Advance the download cursor after extraction. |
| `/rest/services/{serviceId}/FeatureServer/{layerId}/applyEdits` | Reconcile create, update, and delete operations after reconnect. |
| `/rest/services/{serviceId}/FeatureServer/{layerId}/query` | Read back run-tagged create/update records and deterministic delete-target state. |

When readback verification is enabled, the fixture must expose:

- a seeded feature with `objectid = 3` so the delete operation can prove
  pre-sync presence and post-sync removal;
- a writable `honua_acceptance_run` field on the editable layer;
- a writable `status` field that preserves `created-offline` and
  `inspection-complete` after reconnect sync.

If the fixture supports attachment or media readback, capture it as additional
manual evidence. The current automated harness records attachment/media metadata
as a planned operation and embeds its operation id, file name, content type, and
content hash in the evidence JSON.

## Running The Harness

Loopback validation:

```bash
dotnet test tests/Honua.Mobile.ServerIntegration.Tests/Honua.Mobile.ServerIntegration.Tests.csproj --filter DisconnectedFieldWorkflowAcceptanceTests
```

Cloud or staging validation:

```bash
export HONUA_MOBILE_CLOUD_ACCEPTANCE=1
export HONUA_MOBILE_CLOUD_BASE_URL=https://staging-api.honua.io
export HONUA_MOBILE_CLOUD_SERVICE_ID=assets
export HONUA_MOBILE_CLOUD_LAYER_IDS=0
export HONUA_MOBILE_ACCEPTANCE_EVIDENCE_DIR=/tmp/honua-mobile-acceptance-evidence

dotnet test tests/Honua.Mobile.ServerIntegration.Tests/Honua.Mobile.ServerIntegration.Tests.csproj --filter "Category=CloudAcceptance"
```

Use a unique `HONUA_MOBILE_ACCEPTANCE_RUN_ID` for repeated cloud runs when the
server fixture records edit history by run id.

## Evidence Artifacts

Every harness run writes `<run-id>.evidence.json` to the configured artifact
directory. Skipped cloud runs write `cloud-disconnected-field-workflow-skipped.evidence.json`.

Required evidence fields:

| Field | Meaning |
| --- | --- |
| `schemaVersion` | Evidence schema, currently `honua.mobile.disconnected-field-workflow.evidence.v1`. |
| `workflowName` | Always `disconnected-field-workflow`. |
| `runId` | Stable run identifier from config or generated cloud default. |
| `status` | `passed`, `failed`, or `skipped`. |
| `packageId` | Offline package id used in operation payload metadata. |
| `serviceId` and `sourceIds` | Cloud/staging fixture source identifiers. |
| `operationIds` | All planned operation ids, including media metadata. |
| `plannedOperations` | Operation kind, target id, syncability, and metadata for each planned edit or media item. |
| `cursorState` | Replica and server generation cursors after verification. |
| `phases` | Per-phase status, timings, details, and failure category when applicable. |
| `finalState` | Pre-reconnect pending count, final pending count, local feature count, readback counts, delete-target state, and verification notes. |
| `failureCategories` | Structured category definitions for troubleshooting. |

Keep the JSON evidence with any cloud run logs, server fixture seed identifiers,
and optional screenshots from manual device or emulator runs. The automated
non-UI harness does not require screenshots.

## Failure Categories

Failures are written into the failed phase details as `failureCategory`:

| Category | Typical cause | First check |
| --- | --- | --- |
| `configuration` | Missing or inconsistent URL, auth, package id, source id, or run flag. | Confirm all required environment variables and auth mode. |
| `package` | Replica creation, package extraction, or fixture payload download failed. | Confirm the seeded service id, layer ids, and replica endpoint support. |
| `local-cache` | GeoPackage file, cursor, feature cache, or SQLite persistence failed. | Check database path permissions and remaining device or CI disk space. |
| `edit-queue` | A planned operation could not be serialized, queued, claimed, or uploaded as a valid edit. | Inspect `plannedOperations`, operation payload metadata, and sync phase failures. |
| `transport` | Network, TLS/certificate validation, auth, timeout, throttling, or server availability blocked upload/download. | Check cloud health, certificate hostname/SANs for the configured base URL, credentials, logs, and retryable server status codes. |
| `conflict` | Server state rejected an edit due to stale base token or feature version conflict. | Compare fixture seed state, `servergen` cursor, and the conflicting operation id. |

Cloud failures should be reported with the evidence JSON, the full test command,
the exact fixture identifiers, and the server-side correlation or request log
ids when available.

For the current staging closure path, a `transport` failure containing
`RemoteCertificateNameMismatch` means the configured cloud host presented a
certificate for a different hostname. Track that as an infrastructure blocker
instead of changing mobile TLS validation; issue #92 currently points at
`honua-io/honua-server#965` for this case.

## Live Server Integration Workflow

The `.github/workflows/live-server-integration.yml` workflow runs the
`LiveHonuaServerInteractionTests` suite from
`tests/Honua.Mobile.ServerIntegration.Tests/LiveHonuaServerInteractionTests.cs`
against a Docker-hosted Honua server (no staging dependency). It triggers on:

- Every pull request (no `paths:` filter, so the required status check is
  always reported even for docs-only PRs).
- Pushes to `main` / `trunk`.
- `workflow_dispatch` for ad-hoc runs.

The job pre-pulls `honuaio/honua-server:latest` and the PostGIS image with
retry, then lets `LiveHonuaServerFixture` orchestrate the Testcontainers
network. Tests are filtered with
`--filter "FullyQualifiedName~LiveHonuaServerInteractionTests"` and gated on
`HONUA_MOBILE_LIVE_SERVER_TESTS=1`. TRX results, container logs (on failure),
and the acceptance evidence directory are uploaded as a single artifact.

Seed SQL: the suite reads `HONUA_MOBILE_LIVE_SERVER_FIXTURE_SQL` and applies
it via `psql` inside the postgres container. The seed file
(`tests/seed/mobile-offline-demo-v1.sql`) lives in the `honua-server` repo and
is not vendored here. The workflow can optionally fetch it via a sparse
checkout when run with `workflow_dispatch` input `fixture_sql_ref`. Until the
seed source is permanently wired up (token + ref selection on PR runs), the
`Run LiveHonuaServerInteractionTests` step is marked `continue-on-error: true`
so an unseeded run does not block unrelated mobile PRs. When the env var
`HONUA_MOBILE_LIVE_SERVER_FIXTURE_SQL` is supplied, an enforcement step turns
any test failure back into a job failure -- so seeded runs (manual or once the
seed source is wired) are strict. Remove `continue-on-error` from the test
step after PR-trigger runs have a reliable seed source.

The workflow surfaces as the `Live Server Integration` status check. Adding
it to branch protection required checks is handled separately by repo
maintainers (not by this workflow).
