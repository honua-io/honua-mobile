# Mobile Release Promotion

This guide defines the first production promotion lane for Honua mobile
releases. It covers release-owner approval, production tag naming, build
number policy, release notes metadata, rollback, hotfixes, and store
resubmits.

The production lane is separate from debug, beta, and internal distribution.
Use `.github/workflows/android-debug-apk.yml` for sideloadable debug APKs and
future beta/internal workflows for tester distribution. Do not use production
tags or the `mobile-production` GitHub Environment for tester-only builds.

## Production Workflow

Run **Mobile Production Promotion** manually from GitHub Actions. The workflow
validates the requested version metadata, waits for the protected
`mobile-production` GitHub Environment, then creates:

- an annotated production tag
- a GitHub Release for the production promotion
- a `mobile-production-promotion.json` metadata artifact

The workflow records approval and version metadata. Store submission still
belongs to release owners using the signed Android and iOS production
artifacts for that tag.

## GitHub Environment Approval

Create a GitHub Environment named `mobile-production` before using the
workflow for production. Configure it with:

- required reviewers from the mobile release-owner group
- deployment branch or tag restrictions that match the release policy
- environment secrets only when a later store-submit workflow needs them

Reviewers should compare the workflow inputs with the release notes, test
evidence, signed artifact provenance, and issue or PR approval record before
approving the environment gate.

## Tag Naming

Production tags use this format:

```text
mobile-prod-v<MAJOR>.<MINOR>.<PATCH>-android.<ANDROID_BUILD>-ios.<IOS_BUILD>
```

Example:

```text
mobile-prod-v1.4.0-android.10400-ios.10400
```

Rules:

- use production SemVer only, with no prerelease suffix
- keep the `release_version` input equal to the `vX.Y.Z` tag segment
- keep both build number inputs equal to the tag's platform build segments
- never delete, move, or reuse a production tag
- use `mobile-prod-*` tags only for production promotion

## Build Numbers

Android and iOS build numbers must be positive integers and must increase
monotonically across production promotions.

The workflow validates build numbers against existing `mobile-prod-*` tags:

- Android `versionCode` / MAUI `ApplicationVersion` must be greater than the
  highest Android build number already promoted.
- iOS `CFBundleVersion` / MAUI `ApplicationVersion` must be greater than the
  highest iOS build number already promoted.
- Android build numbers must not exceed `2100000000`.
- Honua uses the same upper bound for iOS build numbers to keep the policy
  simple and auditable.

The display version may stay the same for a store resubmit when the store
allows it, but both platform build numbers still need to increase.

## Release Notes Metadata

Use `quality/mobile-release-notes-template.md` for each production promotion.
The filled notes should include:

- production tag
- source commit
- Android package name and build number
- iOS bundle identifier and build number
- release owner
- approval issue or PR
- validation evidence and CI links
- rollout plan, known risks, and rollback plan

Pass an HTTPS URL for the approved notes or release ticket in the
`release_notes_url` workflow input. The promotion workflow copies that URL into
the tag message, GitHub Release, and metadata artifact.

## Promotion Process

1. Build and validate signed Android and iOS release artifacts outside the
   debug and beta/internal lanes.
2. Fill out the release notes template and link the validation evidence.
3. Choose the next production tag and monotonically increasing platform build
   numbers.
4. Run **Mobile Production Promotion** from GitHub Actions.
5. Confirm `acknowledge_production=true` and provide the release notes URL plus
   approval issue or PR.
6. Release owners approve the `mobile-production` environment gate.
7. Confirm that the workflow created the production tag, GitHub Release, and
   promotion metadata artifact.
8. Submit the signed artifacts to Google Play and App Store Connect using the
   approved tag and release notes.

## Rollback

A rollback starts in the stores, not by changing Git tags.

- Pause or halt a phased rollout when the store supports it.
- Remove the production release from sale or deactivate the rollout when that
  is the approved mitigation.
- Keep the production tag and GitHub Release unchanged for audit history.
- Document customer impact, mitigation, and support guidance in the release
  issue.
- To ship a replacement binary, promote a new tag with higher Android and iOS
  build numbers. The source commit may be an older known-good commit if the
  rollback is implemented as a forward promotion.

## Hotfix

For a hotfix:

1. Branch from the affected production tag or the current release branch.
2. Apply only the minimal fix and run the release validation checklist.
3. Bump the patch display version unless release owners approve a store
   resubmit under the same display version.
4. Increase both Android and iOS build numbers.
5. Fill out a new release notes template and link the hotfix issue.
6. Run the production promotion workflow for the new tag.

## Store Resubmit

If a store rejects a binary before customer rollout:

- update store metadata only when no binary changes are needed
- keep the existing production tag when only store metadata changes
- create a new production tag when a new binary is required
- keep the same display version only when the store policy allows it
- increase both platform build numbers for every new binary
- link the rejection, fix evidence, and resubmit decision in the release notes

## PR Body Recommendation

```markdown
## Summary

- Add the manual mobile production promotion workflow.
- Document production versioning, approvals, release notes, rollback,
  hotfixes, and resubmits.

## Testing

- yamllint .github/workflows/mobile-production-promotion.yml
- npx markdownlint-cli2 docs/guides/mobile-release-promotion.md
  quality/mobile-release-notes-template.md

Related to #89
```
