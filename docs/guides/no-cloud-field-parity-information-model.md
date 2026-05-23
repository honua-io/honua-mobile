# No-Cloud Field Parity Information Model

Last reviewed: 2026-05-23.

This handoff describes the information model needed to design Honua field
collection UI without requiring hosted cloud services or new visual design work.
The model is intentionally data-first: UI can be designed from these entities
while SDK and mobile engineers continue implementing import, catalog, lifecycle,
assignment, media, conflict simulation, and export behavior.

Authoritative portable contracts belong in `honua-sdk-dotnet`. Mobile consumes
those contracts and owns native storage, local file placement, permissions,
capture adapters, diagnostics, and runtime state.

## Scope Boundary

In scope:

- Local project package import.
- Offline project and survey catalog.
- Local form/runtime state.
- Record lifecycle, assignments, media metadata, validation, conflicts, and
  export evidence.
- Local fixtures and acceptance evidence that run without external services.

Out of scope:

- Hosted form designer or admin UI.
- Cloud publish/download/sync.
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

## Conflict Simulation

No-cloud acceptance should use deterministic local sync-peer evidence:

- operation id and order;
- package id, binding id, source id, record id;
- operation type: create/update/delete/media;
- local version and simulated remote version;
- conflict cause;
- selected resolution;
- final local state.

UI implication: conflict review needs enough data to explain why the record is
blocked and what the available resolution choices mean.

## Export Evidence

Local export/evidence packages should include:

- package metadata;
- project/catalog snapshot;
- records and geometry;
- lifecycle events;
- validation summaries;
- media manifest and optional selected media files;
- conflict simulation evidence;
- diagnostics and redaction summary.

UI implication: export screens should preview exactly what will leave the
device, including media count, record count, redactions, and validation status.

## Implementation Backlog

Local parity backlog:

- honua-mobile#249: local package import and validation.
- honua-mobile#250: local project/survey catalog lifecycle.
- honua-mobile#251: offline record lifecycle state machine.
- honua-mobile#252: local assignment/task packets.
- honua-mobile#253: local media parity adapters.
- honua-mobile#254: local parity golden fixtures.
- honua-mobile#255: local conflict simulation and sync replay.
- honua-mobile#256: local export/evidence packages.
- honua-mobile#257: no-cloud field day acceptance harness.
- honua-mobile#258: parent tracker.

SDK model dependencies:

- honua-sdk-dotnet#160: local field project package contracts.
- honua-sdk-dotnet#161: non-cloud form/media/value parity contracts.
