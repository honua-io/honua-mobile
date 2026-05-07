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

Register server-upload capture only when the environment has an approved mobile
exception ingestion endpoint. `ServerUpload` still writes sanitized reports to
the same local queue first; the upload worker deletes queued files only after a
successful upload:

```csharp
services.AddHonuaMobileExceptionReporting(new MobileExceptionReportingOptions
{
    Mode = MobileExceptionReportingMode.ServerUpload,
    QueueDirectory = exceptionQueueDirectory,
    UploadEndpoint = new Uri("https://api.honua.example/mobile/exception-reports"),
    MaxUploadBatchSize = 10,
    UploadInitialBackoff = TimeSpan.FromSeconds(30),
    UploadMaxBackoff = TimeSpan.FromMinutes(15),
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

`ServerUpload` mode registers `IMobileExceptionReportUploadWorker`, which can be
called by app lifecycle or background scheduling code when connectivity and
battery policy allow. Each flush is bounded by `MaxUploadBatchSize`. Failed
uploads remain queued and are retried with exponential in-memory backoff between
`UploadInitialBackoff` and `UploadMaxBackoff`; reports are deleted only after
`IMobileExceptionReportUploader.UploadAsync` returns success.

The default `HttpMobileExceptionReportUploader` posts the already-sanitized
`MobileExceptionReport` JSON to the explicit `UploadEndpoint`. The endpoint must
use HTTPS unless it is localhost for development. Apps that need approved
request metadata, such as same-origin authentication headers, should register an
`IMobileExceptionReportUploadRequestCustomizer` instead of creating a parallel
exception-report DTO or hardcoded server client. Tenant routing and ingestion
schema versioning should be added through the uploader boundary once the server
endpoint contract exists.

The FieldCollection app maps the legacy `honua_exception_reporting_mode=Server`
preference to `ServerUpload`, stamps build/device metadata from the mobile build
configuration, and attaches its API key only when the configured upload endpoint
shares the authenticated server origin. It still requires the explicit
`honua_exception_reporting_endpoint` preference; mobile does not assume a server
route before the ingestion contract is defined.

Do not flush from a blocking UI path or synchronously during app startup. Start
flush work from lifecycle/background scheduling code and allow it to stop on
cancellation. Exception reporting must never block app launch, sync, auth
refresh, or field capture.

## Server Dependency

Server ingestion is a separate dependency for issue #91. The mobile repo should
not add a server API client, shared server DTO, or long-lived copied contract for
that endpoint. The server-side slice needs to define the ingestion route, auth
requirements, request headers, retention rules, log sink mapping, searchable
metadata fields, schema versioning, and operational triage behavior. Mobile
upload work should consume that versioned contract/package once it exists.

Recommended PR body note:

```text
Related to #91
```
