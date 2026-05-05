# Mobile DevOps Release Handoff

Issue #82 is ready for release-owner handoff when the repository can show which
mobile lanes exist, who must approve each protected step, and what evidence is
required before beta or store promotion. This guide gives release owners that
packet without claiming that Google Play, Apple Developer, or App Store Connect
setup can be completed inside this repository.

Use this with:

- [Mobile DevOps Builds](mobile-devops-builds.md)
- [Mobile Android Internal Distribution](mobile-android-internal-distribution.md)
- [Mobile TestFlight Builds](mobile-testflight-builds.md)
- [Mobile Beta Feedback Loop](mobile-beta-feedback-loop.md)
- [Mobile Release Promotion](mobile-release-promotion.md)
- [Mobile Store Prerequisites](mobile-store-prereqs.md)
- [mobile release owner handoff checklist](../../quality/mobile-release-owner-handoff.md)

## Workflow Inventory

| Workflow | File | Purpose | Approval Boundary |
| --- | --- | --- | --- |
| Android Debug APK | `.github/workflows/android-debug-apk.yml` | Manual non-production sideload APK for FieldCollection tester validation. | No signing environment; endpoint guard refuses production-looking metadata. |
| Android Internal Distribution | `.github/workflows/android-internal-distribution.yml` | Manual signed Android internal testing or Internal App Sharing upload. | Protected environment selected by the run, normally `android-internal`. |
| iOS TestFlight | `.github/workflows/ios-testflight.yml` | Manual signed iPhone IPA archive and App Store Connect TestFlight upload. | Protected environment selected by the run, normally `ios-testflight`. |
| Mobile Production Promotion | `.github/workflows/mobile-production-promotion.yml` | Manual production tag, GitHub Release, and promotion metadata creation. | Protected `mobile-production` environment. |

The production promotion workflow does not submit binaries to Google Play or
App Store Connect. Release owners still need signed platform artifacts, store
account access, store metadata approval, and rollout decisions outside the repo.

## Protected Environments

Release owners must verify these GitHub Environments before a beta or
production promotion window opens.

| Environment | Required Before | Reviewers | Secrets Scope |
| --- | --- | --- | --- |
| `android-internal` | Android internal testing or Internal App Sharing. | Release manager plus Android store owner. | Android signing and Google Play upload secrets for internal lanes only. |
| `ios-testflight` | TestFlight upload. | Release manager plus Apple owner. | Apple signing, provisioning profile, and App Store Connect upload secrets. |
| `mobile-production` | Production tag and release metadata promotion. | Release manager plus applicable store owner. | No store signing secret is required for the current promotion workflow. |
| `android-production` | Future Google Play production submit or promote lane. | Release manager plus Google Play Console owner. | Production-scoped Android signing and Play upload secrets only when that lane exists. |
| `ios-production` | Future App Store production submit or promote lane. | Release manager plus Apple Developer or App Store Connect owner. | Production-scoped Apple signing and upload secrets only when that lane exists. |

Do not move store credentials to repository-wide secrets. Do not commit
keystores, certificates, provisioning profiles, service account JSON, API keys,
tester email lists, or export compliance answers.

## Owner Verification

Each release window needs named owners. Use groups only when the group has a
clear on-call or decision record.

| Owner | Must Verify | Evidence To Collect |
| --- | --- | --- |
| Release manager | Scope, version, channel, beta window, release notes, approval issue, and final go/no-go. | Filled handoff checklist, linked release notes, GitHub Actions run URLs, approver names, and issue links. |
| GitHub environment owner | Protected environments exist, required reviewers are configured, branch or tag restrictions match policy, and secrets are environment-scoped. | Environment names, reviewer list, restriction summary, secret-name inventory without secret values. |
| Android store owner | Play Console app records, package IDs, tester tracks, Play App Signing, upload service account, and version code policy. | Play app IDs, package names, tester track or Internal App Sharing link, Play release or upload record, version code. |
| Android signing owner | Upload keystore ownership, vault location, rotation path, and GitHub secret freshness. | Key alias, creation or rotation date, vault record reference, environment secret names. |
| Apple owner | Apple Developer team, bundle IDs, App Store Connect app records, TestFlight group, export compliance status, and API key access. | Team ID, bundle IDs, App Store Connect build record, TestFlight group name, compliance decision reference. |
| iOS signing owner | Distribution certificate, provisioning profiles, expiration dates, and GitHub secret freshness. | Certificate expiration, profile names, bundle ID match, vault record reference, environment secret names. |
| QA or beta coordinator | Devices, accounts, install instructions, feedback intake, crash or log collection, and triage ownership. | Beta release summary, device matrix, feedback issue or thread, crash export or diagnostic links. |
| Security owner | Credential exposure response, access review, privacy or permission changes, and emergency rotation. | Rotation decision record, access review date, privacy or permission approval link. |

## Handoff Packet

Before asking for beta or production approval, the release manager assembles one
handoff packet per channel. Store the packet in the release issue, PR, or other
approved release record, not in workflow inputs alone.

Required contents:

- Issue #82 release slice or release ticket link.
- Channel: direct APK, Android internal, TestFlight, or production promotion.
- App target and identifier: `io.honua.mobile.app` or
  `io.honua.mobile.fieldcollection`.
- Source ref and resolved commit SHA.
- Version name and platform build number.
- Target environment and endpoint policy.
- Workflow run URL and artifact or store build identifier.
- Protected environment name and approver record.
- Release notes or beta summary link.
- Validation evidence links.
- Rollback or replacement build plan.
- Remaining external prerequisites from #85 or #87.

For beta channels, start from
`quality/mobile-beta-release-summary-template.md`. For production, start from
`quality/mobile-release-notes-template.md`. For owner-to-owner handoff, use
`quality/mobile-release-owner-handoff.md`.

## Evidence Requirements

Collect evidence before promotion, not after testers find a problem.

| Stage | Minimum Evidence |
| --- | --- |
| Direct APK | Artifact name, metadata JSON, install notes, selected non-production API base URL, commit SHA, tester notes, and smoke result. |
| Android internal | Workflow run URL, signed artifact name, SHA-256 digest, Play channel, version name, version code, package name, protected environment approval, and tester link or track. |
| TestFlight | Workflow run URL, IPA artifact, App Store Connect build number, bundle ID, protected environment approval, TestFlight processing status, and tester group. |
| Production promotion | Approved release notes URL, production tag, release version, Android and iOS build numbers, source commit SHA, approval issue or PR, GitHub Release URL, and promotion metadata artifact. |
| Store rollout | Store submission record, rollout percentage or phased-release setting, store review status, live version, monitoring owner, stop conditions, and customer support notes. |

Every tester-facing build should also tie back to the diagnostic fields in
[Mobile Beta Feedback Loop](mobile-beta-feedback-loop.md): app version, build
number, commit SHA, environment, channel, artifact or store build ID, and
workflow run.

## Promotion Gates

Release owners should treat these as hard gates until an explicit release
decision says otherwise.

| Gate | Blocks | Owner Decision |
| --- | --- | --- |
| #85 Google Play prerequisites | Android internal distribution, Android production submission, and any store rollout using Play Console. | Confirm Play account, app records, package IDs, Play App Signing, upload credentials, tester access, and production promotion rights. |
| #87 App Store Connect prerequisites | TestFlight upload, App Store production submission, and iOS phased release. | Confirm Apple team, bundle IDs, app records, certificates, provisioning profiles, API key, tester group, export compliance, and submission rights. |
| Protected GitHub environments | Any lane that uses signing, upload, or production promotion approval. | Confirm reviewers and restrictions before secrets are added or production metadata is created. |
| Release notes and beta summary | Tester distribution and production promotion. | Confirm the summary matches the build, channel, known risks, rollback plan, and feedback path. |
| Validation evidence | Beta expansion and store promotion. | Confirm CI, smoke, device, security, accessibility, performance, and issue-triage evidence are sufficient for the release scope. |

If #85 or #87 is incomplete, the repository can still produce docs, dry-run
metadata, and non-production debug APKs, but final beta or store promotion
remains blocked by the external account owner.

## Runbook

1. Choose the channel and source ref.
2. Confirm the relevant external prerequisite issue: #85 for Google Play, #87
   for App Store Connect.
3. Confirm the protected GitHub Environment exists and has the right reviewers.
4. Confirm signing and upload secrets are present only in the protected
   environment required for that channel.
5. Run the workflow or identify the already completed workflow run.
6. Record the resolved commit SHA, version, build number, artifact or store
   build ID, and digest when available.
7. Fill the beta summary, production release notes, or release-owner handoff
   checklist.
8. Attach validation evidence and known-risk decisions.
9. Get release-owner approval before sharing the build with testers or creating
   production promotion metadata.
10. Monitor the declared feedback and store channels until ownership is handed
    off or the window closes.

## Stop Conditions

Stop promotion and update the release issue when any of these are true:

- The source ref, commit SHA, artifact, store build, or release notes do not
  match.
- Required protected environment approval is missing or came from the wrong
  owner.
- A required secret is missing from the environment or appears in repository
  scope.
- A debug or beta artifact points at a production-looking endpoint without an
  explicit production workflow.
- #85 or #87 is incomplete for the channel being promoted.
- Store review, export compliance, privacy, permission, or signing ownership is
  unresolved.
- Validation evidence shows a P1/P2 issue, data loss risk, credential exposure,
  or unsupported rollback path.

## Handoff Completion

Issue #82 can be considered handed off for a release slice when release owners
can answer these questions from linked evidence:

- Which workflow produced or promoted the build?
- Which protected environment released secrets or approval?
- Which owner approved the action?
- Which exact commit, version, build number, and artifact or store build is in
  scope?
- Which beta or production evidence was reviewed?
- Which external prerequisite issue, #85 or #87, still gates final promotion?
- Who owns monitoring, rollback, and the next decision?
