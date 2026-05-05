# Mobile Beta Feedback Loop

This guide defines how beta testers report issues from phone builds, how build
metadata is attached to diagnostic evidence, and how maintainers triage crash,
log, and tester feedback for Honua mobile beta releases.

## Tooling Decision

The default path for beta diagnostics is the first-party mobile
exception-to-server flow tracked in #91. Beta builds should capture local
diagnostic events, include build metadata on each event, and forward exception
summaries to Honua server logs when #91 lands.

External crash tooling is deferred. Revisit Sentry, App Center replacement
options, Firebase Crashlytics, or native store crash reports only if #91 does
not meet beta needs for crash grouping, tester identification, privacy review,
offline buffering, or triage latency.

Until #91 is available, release owners must collect tester reports, app logs,
store crash exports, and device details through the intake paths below.

## Beta Channels

Every tester-facing build must have one declared source. The release owner uses
that source to identify how the tester installed the app and where the first
report should arrive.

- Direct APK: source ID `direct-apk`; intake through the linked issue or
  release feedback thread; tester details include APK artifact name, device
  model, and Android version.
- Android internal distribution: source ID `android-internal`; intake through
  Play Console internal test feedback or the linked issue; tester details
  include Play track, version code, and device model.
- TestFlight: source ID `testflight`; intake through TestFlight feedback or the
  linked issue; tester details include TestFlight build number, iOS version,
  and device model.

Do not mix channels in one release summary. If the same commit is shipped
through multiple channels, publish one release summary per channel so triage can
identify the report source without guessing.

## Build Metadata

Crash reports, diagnostic events, tester reports, and release summaries must
include the same metadata fields wherever the channel allows them.

| Field | Description |
| --- | --- |
| `app_name` | App display name shown on the phone |
| `app_version` | Human-readable version, such as `0.0.0-debug.123` |
| `build_number` | Monotonic platform build number or workflow run number |
| `commit_sha` | Full source commit SHA for the build |
| `short_sha` | Short commit SHA used in artifact names |
| `environment` | Target backend environment, such as `dev` or `staging` |
| `channel` | One of `direct-apk`, `android-internal`, or `testflight` |
| `artifact_name` | CI artifact, Play build, or TestFlight build identifier |
| `workflow_run` | GitHub Actions run URL or run ID when built by CI |
| `installed_at` | Tester-provided install date and time, when known |

Android debug APK artifacts already emit related metadata beside the APK as
documented in [Mobile DevOps Builds](mobile-devops-builds.md). Release owners
should copy those values into the release summary and ask testers to include
the artifact name in every report.

## Diagnostic Event Correlation

Diagnostic events must be traceable back to a build, channel, and backend
environment. At minimum, every crash or handled exception event should include:

- Build metadata fields listed above.
- UTC timestamp and device local time zone.
- Platform, OS version, device model, and app process uptime.
- Screen or workflow area, when available.
- Connectivity state and selected server URL host.
- Session ID or installation ID that does not expose personal data.
- Exception type, message summary, stack trace, and handled/unhandled status.

For direct APK installs, the artifact metadata JSON is the source of truth. For
Android internal distribution, Play Console version code and track identify the
build. For TestFlight, Apple build number and TestFlight feedback ID identify
the build.

## Tester Instructions

Release summaries must include tester-facing instructions for the active
channel. Keep the instructions short and specific to the build under test.

### Direct APK

1. Install the APK from the release artifact link supplied by the release owner.
2. Confirm the endpoint shown in the release summary before signing in.
3. Reproduce the issue once if it is safe to do so.
4. File feedback in the linked issue or feedback thread.
5. Include the APK artifact name, app version, device model, Android version,
   steps to reproduce, expected result, actual result, screenshots if useful,
   and the approximate local time of the issue.

### Android Internal Distribution

1. Install or update from the Play internal testing link.
2. Confirm the version code and target environment from the release summary.
3. Use Play tester feedback for crashes or install failures when possible.
4. File workflow bugs in the linked issue or feedback thread.
5. Include the Play track, version code, device model, Android version, steps
   to reproduce, expected result, actual result, and approximate local time.

### TestFlight

1. Install or update from TestFlight.
2. Confirm the TestFlight build number and target environment from the release
   summary.
3. Use TestFlight feedback for screenshots, crashes, or install failures when
   possible.
4. File workflow bugs in the linked issue or feedback thread.
5. Include the TestFlight build number, device model, iOS version, steps to
   reproduce, expected result, actual result, and approximate local time.

## Release Owner Duties

Before sharing a beta build, the release owner must publish a release summary
using [the beta release summary template](../../quality/mobile-beta-release-summary-template.md).
The summary must include install instructions, known limitations, build
metadata, feedback channels, and rollback notes.

After sharing a beta build, the release owner must monitor the declared intake
paths until the beta window closes or ownership is handed off.

## Triage Workflow

1. Confirm the report source: `direct-apk`, `android-internal`, or
   `testflight`.
2. Match the report to build metadata: version, build number, commit SHA,
   environment, artifact name, and workflow run.
3. Classify the report as crash, install failure, data loss risk, sync failure,
   map/display issue, permission issue, workflow bug, or feedback.
4. Attach diagnostic evidence: server log entry, exported crash report, tester
   screenshot, local timestamp, device model, and reproduction steps.
5. Check for duplicate reports from the same build and environment.
6. Assign an owner and severity.
7. If the report needs server support, link the dependent server issue from the
   mobile issue.
8. If the report needs first-party exception forwarding, link #91 and capture
   the missing diagnostic field.
9. Record the triage outcome in the mobile backlog.

## Backlog Follow-Up

Issue #91 owns first-party mobile exception reporting back to Honua server logs.
The mobile beta backlog should keep one explicit decision item open until #91
is validated against beta needs:

- Default: use first-party exception-to-server diagnostics for beta quality
  feedback.
- Revisit external crash tooling only if #91 does not provide actionable crash
  grouping, source-channel correlation, and release-owner triage visibility.
