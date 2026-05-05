# Mobile Exception Reporting

Honua mobile exception reporting is opt-in. The first mobile slice provides a
sanitized local reporting surface and offline queue for MAUI/mobile apps. It
does not define or implement the Honua Server ingestion endpoint.

## Enablement

Register the disabled default when an app wants the reporting abstraction
available without capturing reports:

```csharp
services.AddHonuaMobileExceptionReporting();
```

Register local-only capture when tester consent, app configuration, and the
environment kill switch all allow exception reporting:

```csharp
services.AddHonuaMobileExceptionReporting(new MobileExceptionReportingOptions
{
    Mode = MobileExceptionReportingMode.LocalOnly,
    QueueDirectory = exceptionQueueDirectory,
    Metadata = new MobileExceptionReportMetadata
    {
        AppId = "honua.field",
        AppVersion = appVersion,
        BuildNumber = buildNumber,
        CommitSha = commitSha,
        Branch = branch,
        EnvironmentName = environmentName,
        Platform = platform,
        OsVersion = osVersion,
        DeviceClass = deviceClass,
    },
});
```

Apps can report handled exceptions through `IMobileExceptionReporter` with a
source, operation, correlation id, request id, and small diagnostic properties.
Unhandled exception hooks are available through
`MobileExceptionReportingExceptionHooks.Register()`, but apps should install
them only after consent/configuration has enabled reporting. The hooks record
`AppDomain.CurrentDomain.UnhandledException` and
`TaskScheduler.UnobservedTaskException` into the same local queue.

## Privacy Defaults

Reports are sanitized before they are stored on device. The redactor removes or
masks:

- bearer/basic authorization values, access tokens, refresh tokens, API keys,
  passwords, secrets, and credential-like key/value pairs.
- URL user-info credentials such as `https://user:password@example.test`.
- sensitive URL query parameters such as `token`, `api_key`, `password`,
  `secret`, signatures, and authorization codes.
- precise location properties such as latitude, longitude, GPS, coordinates, and
  location keys unless `IncludePreciseLocation` is explicitly enabled.
- form payload and user-entered field value properties unless
  `IncludeFormPayloads` is explicitly enabled.
- attachment, media, photo, image, and file byte content unless
  `IncludeAttachmentContent` is explicitly enabled.

Even when precise location or form payload capture is explicitly enabled, token
and credential redaction still applies. Do not pass full record payloads,
attachments, raw request bodies, or precise coordinates as routine context.
Prefer stable identifiers, operation names, correlation ids, and coarse state.

## Offline Queue And Retry Expectations

`LocalOnly` mode writes sanitized `MobileExceptionReport` files through
`IMobileExceptionReportQueue`. The file-backed queue trims old reports using
`MaxQueuedReports` and suppresses repeated reports from the same source,
exception type, message, and first stack frame inside `DuplicateWindow`.

The queue is intentionally transport-neutral. A later uploader can read pending
items, attempt delivery when connectivity and battery policy allow, and delete
items only after successful ingestion. Retry/backoff policy should live in that
uploader or a background worker, not in UI flows or app startup. Exception
reporting must never block app launch, sync, auth refresh, or field capture.

## Server Dependency

Server ingestion is a separate dependency for issue #91. The mobile repo should
not add a server API client, shared server DTO, or long-lived copied contract for
that endpoint. The server-side slice needs to define the ingestion route, auth
requirements, retention rules, log sink mapping, searchable metadata fields, and
operational triage behavior. Mobile upload work should consume that versioned
contract/package once it exists.

Recommended PR body note:

```text
Related to #91
```
