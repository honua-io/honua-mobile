# Honua Mobile Release Checklist

Complete all items before publishing a release. Each gate references an
automated or manual quality check; link the evidence (CI run URL, review
comment, etc.) in the Notes column.

---

## CI and Automated Gates

- [ ] All CI gates pass (build, test, gRPC validation, security)
- [ ] .NET SDK train evidence is current: `Directory.Build.props`
      `HonuaSdkDotNetTrainVersion` matches the published `honua-sdk-dotnet`
      train and `quality/release-evidence/` names the release manifest lane
- [ ] Performance budgets within thresholds (`quality/performance-budget.json`)
- [ ] Smoke tests pass (`tests/Honua.Mobile.Smoke.Tests`)
- [ ] MAUI Android API 33 emulator smoke and trim publish pass; direct
      mobile-code trim warnings remain blocking
- [ ] MAUI iOS 17+ simulator build plus trim/NativeAOT publish pass; direct
      mobile-code trim/AOT warnings remain blocking
- [ ] No CodeQL, Trivy, or dependency-audit findings above accepted risk level

## Quality Review

- [ ] Accessibility checklist reviewed for new/changed UI (`quality/accessibility-checklist.md`)
- [ ] No P1/P2 bugs open in the issue tracker
- [ ] Manual exploratory testing completed on target platforms (Android, iOS, Windows)

## Documentation and Release Artifacts

- [ ] CHANGELOG updated with user-facing changes
- [ ] Version number bumped in `Directory.Build.props` / `.csproj` files
- [ ] The `mobile-dotnet-sdk-train` release lane in
      `honua-server/release/honua-2026-05-preview.json` has the mobile commit
      SHA, SDK train, run URL, and waiver state
- [ ] Migration notes documented for breaking API changes (if any)
- [ ] NuGet package metadata reviewed (description, tags, license)
- [ ] NuGet publish release tag is signed and matches `mobile-dotnet-v*`
- [ ] Mobile store prerequisites reviewed for app-store builds
      (`docs/guides/mobile-store-prereqs.md`)
- [ ] Android store prerequisite mapping validation passes
      (`scripts/validate-android-store-prereqs.sh`)
- [ ] iOS store prerequisite mapping validation passes
      (`scripts/validate-ios-store-prereqs.sh`)
- [ ] `trunk` branch protection confirmed: required reviews, required status
      checks, and force-push disabled

## Platform-Specific Validation

- [ ] Android build and smoke test pass on CI
- [ ] Android `PublishTrimmed=true; TrimMode=full` publish has no new direct
      mobile-code trim warnings; upstream package assembly-summary warnings are
      reviewed before release
- [ ] Windows MAUI build passes on CI
- [ ] iOS simulator build verified on Mac-hosted CI
- [ ] iOS `ios-arm64; PublishTrimmed=true; TrimMode=full; PublishAot=true;
      PublishAotUsingRuntimePack=true` publish has no new direct mobile-code
      trim/AOT warnings; upstream package assembly-summary warnings are
      reviewed before release

## Final Approval

| Approver | Date | Result |
|----------|------|--------|
|          |      |        |
