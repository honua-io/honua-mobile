# Mobile Beta Release Summary Template

Use one copy of this template per beta channel. Do not combine direct APK,
Android internal distribution, and TestFlight instructions in a single summary.

## Release

| Field | Value |
| --- | --- |
| App | |
| Channel | `direct-apk`, `android-internal`, or `testflight` |
| Target environment | |
| App version | |
| Build number | |
| Commit SHA | |
| Short SHA | |
| Artifact or store build ID | |
| GitHub Actions run | |
| Release owner | |
| Beta window | |
| Feedback thread or issue | |

## Audience

- Test group:
- Required accounts or permissions:
- Devices or OS versions requested:

## Install Instructions

### Direct APK

1. Download the APK from:
2. Confirm the artifact name matches:
3. Enable install from trusted source if Android prompts for it.
4. Install the APK.
5. Open the app and confirm the target environment is:

### Android Internal Distribution

1. Open the Play internal testing link:
2. Join the internal test track if prompted.
3. Install or update the app from Google Play.
4. Confirm the version code is:
5. Open the app and confirm the target environment is:

### TestFlight

1. Open the TestFlight invitation link:
2. Install or update the app in TestFlight.
3. Confirm the TestFlight build number is:
4. Open the app and confirm the target environment is:

## Scope Under Test

- Primary workflows:
- Regression checks:
- Out of scope:

## Known Limitations

- Limitation:
- Workaround:
- Issue link:

## Feedback Channels

- Crashes or install failures:
- Workflow bugs:
- Screenshots or screen recordings:
- Urgent escalation:

Include these details in every report:

- Channel: `direct-apk`, `android-internal`, or `testflight`
- App version and build number
- Artifact name, Play version code, or TestFlight build number
- Device model and OS version
- Approximate local time of the issue
- Steps to reproduce
- Expected result
- Actual result
- Screenshots or recordings, when useful

## Diagnostic Metadata

The release owner must verify that crash reports, diagnostic logs, and tester
reports can be correlated with:

- App version:
- Build number:
- Commit SHA:
- Environment:
- Channel:
- Artifact or store build ID:
- Workflow run:

First-party exception-to-server reporting in #91 is the default diagnostic path.
External crash tooling should be revisited only if #91 does not satisfy beta
crash grouping, source-channel correlation, or triage latency needs.

## Rollback Note

- Previous known-good build:
- Rollback install link or store action:
- Data compatibility notes:
- Tester notification owner:

## Triage Owner Notes

- Intake source checked:
- Duplicate reports checked:
- Server log or crash export linked:
- Backlog issue:
- Severity:
- Owner:
