# Honua Mobile Release Checklist

Complete all items before publishing a release. Each gate references an automated or manual
quality check; link the evidence (CI run URL, review comment, etc.) in the Notes column.

---

## CI and Automated Gates

- [ ] All CI gates pass (build, test, gRPC validation, security)
- [ ] Performance budgets within thresholds (`quality/performance-budget.json`)
- [ ] Smoke tests pass (`tests/Honua.Mobile.Smoke.Tests`)
- [ ] No CodeQL or dependency-audit findings above accepted risk level

## Quality Review

- [ ] Accessibility checklist reviewed for new/changed UI (`quality/accessibility-checklist.md`)
- [ ] No P1/P2 bugs open in the issue tracker
- [ ] Manual exploratory testing completed on target platforms (Android, iOS, Windows)

## Documentation and Release Artifacts

- [ ] CHANGELOG updated with user-facing changes
- [ ] Version number bumped in `Directory.Build.props` / `.csproj` files
- [ ] Migration notes documented for breaking API changes (if any)
- [ ] NuGet package metadata reviewed (description, tags, license)

## Platform-Specific Validation

- [ ] Android build and smoke test pass on CI
- [ ] Windows MAUI build passes on CI
- [ ] iOS build verified (manual or Mac-hosted agent)

## Final Approval

| Approver | Date | Result |
|----------|------|--------|
|          |      |        |
