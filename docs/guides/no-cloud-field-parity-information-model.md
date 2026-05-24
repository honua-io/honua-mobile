# No-Cloud Field Parity Information Model

Last reviewed: 2026-05-24.

This handoff describes the information model needed to design Honua field
collection UI without requiring hosted cloud services or new visual design work.
The model is intentionally data-first: UI can be designed from these entities
now that package intake, catalog state, lifecycle, assignment, media, conflict
simulation, export, and Work tab behavior have mobile-owned local coverage.

Authoritative portable contracts belong in `honua-sdk-dotnet`. Mobile consumes
those contracts and owns native storage, local file placement, permissions,
capture adapters, diagnostics, and runtime state.

## Scope Boundary

In scope:

- Local project package import and direct manifest/artifact download.
- Offline project and survey catalog.
- Local form/runtime state.
- Record lifecycle, assignments, media metadata, validation, conflicts, and
  export evidence.
- Mobile Work tab integration for local package intake, assignment actions,
  record open routing, lifecycle actions, export, and native share handoff.
- Local fixtures and acceptance evidence that run without external services.

Out of scope:

- Hosted form designer or admin UI.
- Hosted package catalog, publish APIs, and full cloud sync.
- Store release setup.
- New visual system or screen layouts.

## Top-Level Package

`FieldProjectPackage` is the root model for a no-cloud project handoff.

Core fields:

- `schemaVersion`: contract version, currently
  `honua.field-project-package.v1`.
- `projectId`, `name`, `version`, `description`, `generatedAtUtc`.
- `sources`: feature source descriptors from `Honua.Sdk.Abstractions`.
- `forms`: SDK `FormDefinition` entries.
- `bindings`: joins forms to sources and offline packages.
- `offlinePackages`: local artifact references such as GeoPackage, tile, media,
  or scene packages.
- `mediaPolicy`: package-wide and per-field capture/export rules.
- `lifecyclePolicy`: supported record statuses and transitions.
- `taskPackets`: optional offline assignment packets.
- `metadata`: non-UI operational metadata.

UI implication: the first screen can be a local project/survey catalog sourced
from imported `FieldProjectPackage` records. It does not need cloud discovery.

## Direct Package Intake

Mobile supports two local-first package intake paths:

- `LocalFieldProjectPackageImportService` imports an SDK
  `FieldProjectPackage`, validates bindings and artifacts, copies package
  artifacts into device-local storage, creates local catalog/layer state, and
  stores task packets for local assignments.
- `LocalFieldProjectPackageDownloadService` downloads a manifest URI plus
  package-relative offline artifacts into a local download cache, applies
  same-origin auth headers through `FieldProjectPackageDownloadAuthHeader`, and
  then hands off to the import service.

The downloader intentionally does not define a mobile-local Honua Server package
API client. Hosted package catalog and publish semantics remain server/SDK
work; mobile owns direct URI intake, safe relative artifact resolution, local
cache placement, diagnostics, and app wiring.

UI implication: a no-cloud Work tab can offer file import and direct manifest
URL download without waiting on hosted cloud package discovery.

## Sources And Bindings

`SourceDescriptor` describes where records come from and how edits should be
interpreted. For no-cloud work, this still matters because local packages need
stable layer/source identity.

`FieldProjectBinding` connects:

- one `formId`;
- one `sourceId`;
- optional `offlinePackageId`;
- optional `SourceQuery`;
- display and duplicate-key field ids;
- geometry and editability flags.

UI implication: a survey tile, record list, map layer, and capture action should
be driven by a binding, not by hardcoded source ids.

## Forms

`FormDefinition` remains the source of form structure:

- sections and repeatable sections;
- field ids, labels, help text, and source field names;
- field type, choices, choice set id, referenced form id;
- required flags and validation constraints;
- visibility rules and calculated expressions;
- media capture policy for media-like fields.

Relevant field types for parity:

- text, numeric, date, time, date/time, yes/no;
- single choice, multiple choice, classification;
- address, hyperlink, record link, calculated;
- photo, video, audio, signature, sketch, barcode, file, location.

UI implication: form screens should render from `FormDefinition`; designers can
choose controls per `FormFieldType`, but field identity, validation, visibility,
and record values are data model concerns.

## Record Runtime State

`FieldRecord` is the portable record payload:

- `recordId`, `formId`;
- `values` keyed by field id;
- `media` portable metadata;
- `location`;
- `status`, `assignedUserId`;
- timestamps for create, submit, complete.

Advanced portable values:

- `FieldAddressValue`: structured address and optional geocode point.
- `FieldRecordLinkValue`: target form/source/record and display label.
- `FieldBarcodeValue`: decoded value, format, scan time.
- `FieldMediaAttachment`: media type, file name, content type, size, capture
  location/time, duration, SHA-256, face-blur flag, optional GPS track.
- `FieldGpsTrackReference`: portable GPS track metadata for audio/video.

UI implication: draft state, record detail, media galleries, and export previews
should display these fields without depending on local file paths.

## Lifecycle

No-cloud parity needs record states beyond raw edit queue status.

Supported statuses:

- `Draft`
- `ReadyToSubmit`
- `Submitted`
- `Rejected`
- `Approved`
- `Reopened`
- `Deleted`

Default useful transitions:

- `Draft -> ReadyToSubmit`
- `ReadyToSubmit -> Submitted`
- `Draft -> Submitted`
- `Submitted -> Approved`
- `Submitted -> Rejected`
- `Rejected -> Reopened`
- `Reopened -> ReadyToSubmit`
- `Reopened -> Submitted`
- `Approved -> Reopened`
- `Draft/Submitted/Reopened/Rejected -> Deleted`

UI implication: record list filters, badges, edit affordances, and export
eligibility should be based on `FieldRecordLifecyclePolicy`, not fixed labels.

## Assignments

`FieldTaskPacket` groups local assignments. Each `FieldAssignment` includes:

- `assignmentId`;
- `bindingId`;
- `assigneeUserId` or `crewId`;
- priority: `Low`, `Normal`, `High`, `Urgent`;
- status: `NotStarted`, `InProgress`, `Blocked`, `Complete`, `Canceled`;
- optional due date;
- optional work query;
- linked record ids;
- metadata.

UI implication: an assignment inbox can be local-only. It should filter by
assignee, crew, priority, due date, status, binding, and map/source query.

## Media Policy

`FieldProjectMediaPolicy` defines package-level defaults:

- allowed content types;
- max attachment bytes;
- face blur default;
- capture location default;
- GPS track behavior for timed media;
- per-field requirements.

`FieldMediaRequirement` defines per-form/field limits:

- form id and field id;
- media type;
- min/max attachment count;
- allowed content types.

UI implication: capture buttons, attachment warnings, and submit blocking should
be derived from policy and validation, not from screen-specific rules.

Mobile-owned `AttachmentPayloadKind` values currently distinguish `File`,
`Photo`, `Signature`, `Video`, `Audio`, `Sketch`, and `Barcode` so local
storage, diagnostics, and export can preserve media intent without relying on a
cloud attachment service.

## Local Catalog State

Mobile-owned catalog state should wrap the portable package with runtime facts:

- runtime model: `FieldProjectCatalogEntry`;
- UI-facing projection: `FieldProjectInfo`;
- persisted table: `field_project_catalog`;
- installed, invalid, stale, archived, removable;
- local storage paths;
- package size, media size, and cache size;
- validation diagnostics;
- last opened, last validation, last local acceptance/simulation run, last export;
- local import source and package digest.

UI implication: catalog cards/lists should distinguish portable package metadata
from device-local operational state.

## Golden Fixtures

The local form parity fixture suite is represented by
`LocalFormParityGoldenFixtureTests` and emits
`honua.mobile.form-parity-golden-fixtures.evidence.v1` evidence during CI. The
current fixtures cover:

- inspection workflow: required rules, conditional visibility, calculated
  values, media minimums, inline choices, and draft restore;
- asset inventory: barcode capture, inline choice sets, record-link value
  capture, required rules, and draft restore;
- incident report: location, signature media, conditional injury notes,
  required rules, and draft restore;
- repeat-heavy survey: repeat groups, repeat-scoped validation, repeat-scoped
  media, calculated repeat values, and draft restore.

Unsupported or package-version-gated fixture capabilities are explicitly listed
in the evidence as follow-ups, currently shared choice-set ids, record-link
target metadata, richer media capture policy fields, full XLSForm/Arcade
expression parity, rejected-media fixtures, and nested-repeat scenario coverage.

UI implication: form design can treat these fixture records as no-cloud parity
examples, while unsupported fixture items remain visible as SDK/mobile backlog
instead of being hidden by the runtime.

## Conflict Simulation

No-cloud acceptance should use deterministic local sync-peer evidence:

- `LocalReplayFieldSyncPeer`: in-process pull/push/attachment sync peer used
  only for local replay and CI. It is configured as a sync transport but never
  connects to a cloud endpoint. It can replay server changes, accept/reject
  pending feature edits, and simulate retryable attachment failures.
- `LocalFieldConflictReplayHarness`: creates local fixture edits, replays a
  simulated remote update/delete through `GeoPackageSyncService`, applies the
  selected resolution, and emits evidence.
- evidence schema `honua.mobile.local-conflict-replay.evidence.v1` with run id,
  no-cloud flags, layer/source ids, record id, local/server versions, operation
  event order, conflict cause, selected resolution, final record state, and
  diagnostic conflict/pending counts.
- conflict evidence must use redacted local/server JSON from
  `DiagnosticRedactor` and must not include secrets or local filesystem paths.

UI implication: conflict review needs enough data to explain why the record is
blocked and what the available resolution choices mean.

## Export Evidence

Local export/evidence packages should include:

- `honua-evidence.json`: no-cloud export manifest with format version,
  record/media/conflict counts, validation summary, redaction flags, diagnostics,
  and project catalog match.
- `records.csv`: flat attribute export with pending state, pending operations,
  geometry type, attachment counts, and redacted sensitive values.
- `records.geojson`: geometry export with the same pending state and sanitized
  attribute payload.
- `attachments-manifest.json`: attachment metadata with local paths redacted,
  remote URLs stripped of query/fragment secrets, payload kind, sync state,
  retry/error evidence, and copied media relative paths when content is included.
- `media/`: optional copied local media files selected from existing device
  paths. Missing or deleted media stays metadata-only.
- local project catalog export timestamp via
  `field_project_catalog.last_export_at_utc` when the exported layer matches the
  local project `service_id`, `project_id`, or package id.

UI implication: export screens should preview exactly what will leave the
device, including media count, record count, redactions, and validation status.

## Field-Day Acceptance

The no-cloud product acceptance harness is represented by
`NoCloudFieldDayAcceptanceHarnessTests` and emits
`honua.mobile.no-cloud-field-day.evidence.v1` evidence. The local workflow runs
from an empty GeoPackage and covers:

- local project catalog install/open state;
- package import diagnostics and local package handoff;
- assignment packet import, inbox filtering, and local status completion;
- record lifecycle transition and lifecycle metadata export;
- form validation and calculated field application;
- local record collection into GeoPackage storage;
- local media metadata plus exportable device media content;
- deterministic conflict replay through `LocalReplayFieldSyncPeer`;
- local export package generation and catalog export timestamp marking.

The evidence includes step-level status and artifact names. Current field-day
evidence should not contain `follow-up` steps for assignment packets or record
lifecycle transitions; those local adapters are now covered by package import,
assignment, lifecycle, Work tab, and export tests.

UI implication: a field-day review screen can be designed around step status,
artifact links, counts, and local evidence without requiring hosted acceptance
infrastructure.

## Current Implementation Readiness

This table is the practical UI-design handoff for the current mobile branch.
`Ready` means the data shape exists in mobile code and has local automated
coverage. `SDK-gated` now applies only to remaining package-version or hosted
contract gaps; mobile should wait for published SDK/server contracts instead of
adding duplicate DTOs locally.

| Area | UI-facing data source | Current status | Automated evidence |
| --- | --- | --- | --- |
| Local project catalog | `FieldProjectInfo`, `FieldProjectCatalogEntry`, `field_project_catalog` | Ready | `FieldCollectionMetadataServiceTests`, `NoCloudFieldDayAcceptanceHarnessTests` |
| Form runtime and validation | SDK `FormDefinition`, `FormData`, `FieldRecord`, `MobileFormRuleRuntime` | Ready for current SDK package; newer media policy/record-link metadata is SDK-gated | `MobileFormRuntimeTests`, `LocalFormParityGoldenFixtureTests` |
| Media metadata | `AttachmentInfo`, `AttachmentPayloadKind`, SDK `FieldMediaAttachment` | Ready for local metadata, required counts, payload kind, export, and retry evidence; capture policy fields are SDK-gated | `FieldCollectionAttachmentServiceTests`, `LocalRecordExportServiceTests`, `GeoPackageSyncServiceTests` |
| Conflict review | `ConflictInfo`, `OfflineConflictReviewItem`, `LocalFieldConflictReplayResult` | Ready for local replay, manual/deferred state, accept-server resolution, retryable media failure evidence | `GeoPackageSyncServiceTests` |
| Export preview | `LocalRecordExportResult`, `attachments-manifest.json`, `honua-evidence.json` | Ready | `LocalRecordExportServiceTests` |
| Field-day acceptance | `honua.mobile.no-cloud-field-day.evidence.v1` | Ready as local acceptance harness with package, assignment, lifecycle, media, conflict, and export steps | `NoCloudFieldDayAcceptanceHarnessTests` |
| Package import | SDK `FieldProjectPackage` and validation result | Ready for local manifest import and diagnostics | `LocalFieldProjectPackageImportServiceTests`, `FieldOperationsViewModelTests` |
| Package download | `LocalFieldProjectPackageDownloadRequest`, downloaded manifest/artifact cache, import result | Ready for direct manifest URI intake and safe relative artifact download | `LocalFieldProjectPackageDownloadServiceTests`, `FieldOperationsViewModelTests` |
| Lifecycle transitions | SDK `FieldRecordLifecyclePolicy` and `RecordStatus` | Ready for local transition policy, edit affordances, lifecycle metadata, and export evidence | `LocalFieldRecordLifecycleServiceTests`, `NoCloudFieldDayAcceptanceHarnessTests` |
| Assignment/task packets | SDK `FieldTaskPacket`, `FieldAssignment` | Ready for local task packet import, filtering, status updates, record routing, and Work tab actions | `LocalFieldProjectPackageImportServiceTests`, `FieldOperationsViewModelTests`, `NoCloudFieldDayAcceptanceHarnessTests` |
| Work tab | `FieldOperationsViewModel`, `FieldOperationsPage` | Ready for package import/download, assignment actions, record open routing, export, and share handoff | `FieldOperationsViewModelTests` |

UI design can proceed now for:

- local project/survey catalog cards and filters;
- form rendering, repeat sections, validation errors, calculated values, and
  draft restore states supported by the current SDK package;
- media galleries and export/media count previews;
- conflict review lists and resolution evidence;
- export preview/details;
- field-day evidence review;
- package import/download diagnostics;
- assignment inbox and assignment completion state;
- lifecycle badges, transition actions, and lifecycle-gated edit affordances;
- Work tab flows for local package intake, assignments, record routing, export,
  and share handoff.

UI design should model these as upcoming contract-backed surfaces, but avoid
assuming mobile-local DTOs or hosted APIs until the owning repo ships them:

- hosted cloud package catalogs, package publish, and supervisor/admin package
  workflows;
- richer media capture policy controls;
- shared choice-set and record-link target metadata.

## Implementation Backlog

Closed local parity backlog:

- honua-mobile#249: local package import and validation.
- honua-mobile#250: local project/survey catalog lifecycle.
- honua-mobile#251: offline record lifecycle state machine.
- honua-mobile#252: local assignment/task packets.
- honua-mobile#253: local media parity adapters.
- honua-mobile#254: local form parity golden fixtures.
- honua-mobile#255: local conflict simulation and sync replay harness.
- honua-mobile#256: local export/evidence package generation.
- honua-mobile#257: no-cloud field day product acceptance harness.
- honua-mobile#258: no-cloud/no-design parity parent tracker.

Current remaining parity blockers are outside this no-cloud/no-design local
scope:

- honua-mobile#92 remains open for final hosted cloud acceptance and is blocked
  on honua-server#965 DNS/TLS remediation for `staging-api.honua.io`.
- honua-mobile#225 remains open for AR/3D GA closure until ARCore/ARKit adapter
  evidence and physical-device validation are attached.
- Hosted designer, admin, supervisor review, hosted export/reporting, tenancy,
  and RBAC parity remain server/admin/SDK-owned work consumed by mobile through
  published contracts.

Closed SDK contract inputs:

- honua-sdk-dotnet#160: local field project package contracts.
- honua-sdk-dotnet#161: non-cloud form/media/value parity contracts.
- honua-sdk-dotnet#162: SDK PR that landed the local project package contracts
  consumed by mobile package import/download, assignment, and lifecycle
  adapters.
