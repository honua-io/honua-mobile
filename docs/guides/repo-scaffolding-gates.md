# Repo Scaffolding Gates

This runbook covers the repository scaffolding checks for honua-server #826.
It is intentionally limited to mobile-owned CI, package metadata, release
documentation, and smoke-test commands. It does not introduce server API
clients or duplicate SDK-owned contracts.

## Automated Gate

Run the focused guardrail tests before changing release workflow, package,
license, or dependency automation files:

```bash
dotnet test tests/Honua.Mobile.Smoke.Tests/Honua.Mobile.Smoke.Tests.csproj --filter RepoScaffolding
```

These tests verify:

- `publish-dotnet-mobile.yml` publishes only from signed
  `mobile-dotnet-v*` release tags and keeps manual runs dry-run only.
- Android API 33 emulator smoke, Android trim smoke, iOS 17+ simulator smoke,
  iOS trim smoke, and iOS AOT smoke remain wired in CI.
- `Honua.Mobile.Sdk`, `Honua.Mobile.Offline`, and `Honua.Mobile.Maui` keep
  Apache-2.0 NuGet metadata and package README inclusion.
- Dependabot covers NuGet, npm, and GitHub Actions, while CI uploads Trivy
  SARIF for high and critical findings.
- README keeps the honua-server #811 roadmap link.

## Branch Protection

Branch protection is a live GitHub repository setting, so it cannot be fully
validated from a checkout without GitHub credentials. The repository's current
default branch is `main`; pass `trunk` if the default branch is renamed.
Release owners can run:

```bash
GH_TOKEN=<token-with-branch-protection-read> bash scripts/verify-branch-protection.sh main
```

On GitHub Actions, the CI workflow passes the pushed branch name with
`GITHUB_REPOSITORY` already set. The token must be allowed to read branch
protection for the repository. The script fails unless the target branch
requires approving reviews, requires status checks, and has force pushes
disabled.

CI runs this check only when `HONUA_VERIFY_BRANCH_PROTECTION=true` is configured
as a repository variable and `HONUA_BRANCH_PROTECTION_READ_TOKEN` is available
as a repository secret. Leave the variable unset until the release-owner or repo
admin token is ready.

## Platform CI Smoke Commands

The full Android emulator and iOS simulator/AOT checks require hosted runners
with Android SDK, Xcode, and MAUI workloads. Use the repo-native CI workflow for
the complete gate:

```bash
gh workflow run ci.yml
```

Local equivalents for maintainers with platform tooling installed:

```bash
dotnet workload install maui-android
dotnet publish apps/Honua.Mobile.App/Honua.Mobile.App.csproj --configuration Release --framework net10.0-android /p:PublishTrimmed=true /p:TrimMode=full /p:TreatWarningsAsErrors=true /p:WarningsNotAsErrors=IL2104 /p:AndroidPackageFormat=apk
dotnet test tests/Honua.Mobile.Smoke.Tests/Honua.Mobile.Smoke.Tests.csproj --configuration Release
```

```bash
dotnet workload install maui
dotnet publish apps/Honua.Mobile.App/Honua.Mobile.App.csproj --configuration Release --framework net10.0-ios /p:RuntimeIdentifier=ios-arm64 /p:PublishTrimmed=true /p:TrimMode=full /p:PublishAot=true /p:PublishAotUsingRuntimePack=true /p:EnableCodeSigning=false /p:ArchiveOnBuild=false /p:TreatWarningsAsErrors=true '/p:WarningsNotAsErrors=IL2104;IL3053'
dotnet test tests/Honua.Mobile.Smoke.Tests/Honua.Mobile.Smoke.Tests.csproj --configuration Release
```

Optional live Honua smoke queries must use the existing live-image or configured
server harness. Set these variables only for live server coverage:

```bash
HONUA_MOBILE_SMOKE_BASE_URL=https://<honua-server>
HONUA_MOBILE_SMOKE_SERVICE_ID=<service-id>
HONUA_MOBILE_SMOKE_LAYER_ID=<layer-id>
HONUA_MOBILE_SMOKE_API_KEY=<optional-api-key>
dotnet test tests/Honua.Mobile.Smoke.Tests/Honua.Mobile.Smoke.Tests.csproj --filter LiveFeatureQuery
```

## Package Publish Dry Run

Manual validation can build and pack the four NuGet packages without publish:

```bash
gh workflow run publish-dotnet-mobile.yml -f version=0.1.0-alpha.1 -f dry_run=true
```

Publishing remains blocked for manual runs. Actual package publish happens only
when a signed `mobile-dotnet-v*` release tag is pushed.
