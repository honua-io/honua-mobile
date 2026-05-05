# Mobile Release Owner Handoff

Use one copy per channel or promotion decision. Link the completed checklist
from the release issue or approval PR.

## Release Scope

- Issue or release ticket:
- Channel: direct APK, Android internal, TestFlight, production promotion, or
  store rollout
- App target:
- Package or bundle ID:
- Source ref:
- Commit SHA:
- Version name:
- Android build number:
- iOS build number:
- Target environment:
- Release owner:
- Monitoring owner:
- Rollback owner:

## Workflow Evidence

- Workflow name:
- Workflow file:
- GitHub Actions run:
- Protected environment:
- Approver:
- Artifact name:
- Artifact SHA-256:
- Store build ID or track:
- Release notes or beta summary:
- Promotion tag or GitHub Release:

## External Prerequisites

- [ ] #85 Google Play prerequisites are complete or not required for this
      channel.
- [ ] #87 App Store Connect prerequisites are complete or not required for
      this channel.
- [ ] Store account owner has approved package or bundle identifiers.
- [ ] Tester group owner has approved tester access.
- [ ] Signing owner has confirmed current signing assets and rotation path.
- [ ] GitHub environment owner has confirmed reviewer and secret scope.

## Validation Evidence

- [ ] CI and build validation:
- [ ] Android smoke or install validation:
- [ ] iOS smoke or install validation:
- [ ] Device and OS coverage:
- [ ] Accessibility review:
- [ ] Performance or battery review:
- [ ] Security or privacy review:
- [ ] Crash, log, or diagnostic evidence path:
- [ ] Open P1/P2 issue review:

## Approval Decision

| Owner | Name | Decision | Date | Evidence Link |
| --- | --- | --- | --- | --- |
| Release manager | | | | |
| Android store owner | | | | |
| Apple owner | | | | |
| QA or beta coordinator | | | | |
| Security owner | | | | |

## Rollout And Monitoring

- Initial audience:
- Tester or customer notification:
- Success signals:
- Stop conditions:
- Feedback intake:
- Store monitoring:
- Support notes:
- Rollback or replacement build plan:

## Handoff Notes

- Remaining decisions:
- Known risks:
- Follow-up issues:
