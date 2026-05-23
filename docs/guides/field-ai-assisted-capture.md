# AI-Assisted Field Capture Hooks

Honua mobile exposes opt-in AI-assisted capture hooks for field forms without
owning portable model execution contracts. Mobile owns the capture UX, local
media paths, attachment state, privacy gating, and provider adapter points. SDK
or server packages should own stable request/response contracts and model
execution.

## Mobile-Owned Flow

- `IMobileAiCaptureService` is the form workflow entry point.
- `IMobileAiCaptureProvider` is the host adapter for voice-to-fields,
  photo-to-fields, and media redaction/enrichment.
- `MobileAiCapturePolicy` keeps AI assistance opt-in. The reference form edit
  workflow prompts before first use and keeps media redaction disabled unless
  assistance is enabled.
- `SettingsMobileAiCaptureQueue` stores only sanitized intent when a provider is
  unavailable: layer id, feature id, target field keys, attachment ids, requested
  capabilities, and timestamps. It does not store transcripts, raw media,
  current field values, local file paths, or provider secrets.

## Form Suggestions

The record edit workflow can request AI field suggestions and then apply or
reject them. Suggestions target mobile form value keys, including repeat section
keys, so the UI can apply values without redefining SDK form schemas.

Provider adapters receive current form values and local attachment descriptors at
runtime. They should avoid logging request payloads and should return only field
suggestions that the user can inspect before applying.

## Media State Before Sync

Captured photo attachments can carry `MobileAiMediaState` with redaction and
enrichment status. The edit workflow surfaces this state beside the sync status
before upload, and the SDK media attachment is marked `RequiresFaceBlur` when
the mobile AI state requires it.

## Diagnostics

Diagnostics redaction treats AI capture payload names as sensitive. Voice
transcripts, raw media payloads, local paths, biometric fields, and face
embeddings are redacted before export, copy, or report operations.
