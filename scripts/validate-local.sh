#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
configuration="${CONFIGURATION:-Release}"
solution="${HONUA_MOBILE_VALIDATION_SOLUTION:-${repo_root}/Honua.Mobile.sln}"
nuget_config="${repo_root}/NuGet.config"
run_dotnet=true
run_format=true
run_npm=true
run_npm_ci=true

usage() {
  cat <<'EOF'
Usage: scripts/validate-local.sh [options]

Runs the local Honua Mobile validation baseline:
  - dotnet restore/build/test Honua.Mobile.sln
  - dotnet format checks for core source projects
  - Honua.Mobile.Smoke.Tests
  - npm ci/build/test for src/Honua.Embed

Options:
  --configuration <name>  Build configuration. Default: Release.
  --skip-dotnet          Skip dotnet restore/build/test/format/smoke.
  --skip-format          Skip dotnet format verification.
  --skip-npm             Skip all npm embed validation.
  --skip-npm-ci          Skip npm ci and use existing node_modules.
  -h, --help             Show this help.

GitHub Packages:
  Fresh checkouts need a GitHub token with read:packages access to honua-io
  packages. Set HONUA_GITHUB_PACKAGES_USER and HONUA_GITHUB_PACKAGES_TOKEN.
  The script writes credentials only to a temporary NuGet.config and removes it
  on exit.
EOF
}

fail() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

step() {
  printf '\n==> %s\n' "$*"
}

run() {
  printf '+'
  printf ' %q' "$@"
  printf '\n'
  "$@"
}

xml_escape() {
  local value="$1"
  value=${value//&/&amp;}
  value=${value//</&lt;}
  value=${value//>/&gt;}
  value=${value//\"/&quot;}
  value=${value//\'/&apos;}
  printf '%s' "${value}"
}

make_nuget_config() {
  local token="${HONUA_GITHUB_PACKAGES_TOKEN:-}"
  local user="${HONUA_GITHUB_PACKAGES_USER:-${GITHUB_ACTOR:-}}"

  if [[ -z "${token}" ]]; then
    printf '%s' "${nuget_config}"
    return
  fi

  if [[ -z "${user}" ]]; then
    fail "HONUA_GITHUB_PACKAGES_USER is required when HONUA_GITHUB_PACKAGES_TOKEN is set."
  fi

  local temp_dir
  temp_dir="$(mktemp -d "${TMPDIR:-/tmp}/honua-mobile-validation.XXXXXX")"
  cleanup_paths+=("${temp_dir}")

  local config_path="${temp_dir}/NuGet.config"
  local escaped_user
  local escaped_token
  escaped_user="$(xml_escape "${user}")"
  escaped_token="$(xml_escape "${token}")"

  local old_umask
  old_umask="$(umask)"
  umask 077
  cat >"${config_path}" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="github-honua" value="https://nuget.pkg.github.com/honua-io/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github-honua>
      <add key="Username" value="${escaped_user}" />
      <add key="ClearTextPassword" value="${escaped_token}" />
    </github-honua>
  </packageSourceCredentials>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="github-honua">
      <package pattern="Geospatial.Grpc" />
      <package pattern="Honua.Core" />
      <package pattern="Honua.Mobile.*" />
      <package pattern="Honua.Sdk.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF
  umask "${old_umask}"

  printf '%s' "${config_path}"
}

cleanup_paths=()
cleanup() {
  local path
  for path in "${cleanup_paths[@]:-}"; do
    rm -rf "${path}"
  done
}
trap cleanup EXIT

while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration)
      [[ $# -ge 2 ]] || fail "--configuration requires a value."
      configuration="$2"
      shift 2
      ;;
    --skip-dotnet)
      run_dotnet=false
      shift
      ;;
    --skip-format)
      run_format=false
      shift
      ;;
    --skip-npm)
      run_npm=false
      shift
      ;;
    --skip-npm-ci)
      run_npm_ci=false
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      fail "Unknown option: $1"
      ;;
  esac
done

cd "${repo_root}"

if [[ "${run_dotnet}" == "true" ]]; then
  restore_config="$(make_nuget_config)"

  if [[ -z "${HONUA_GITHUB_PACKAGES_TOKEN:-}" ]]; then
    step "Using existing NuGet credentials"
    cat <<'EOF'
No HONUA_GITHUB_PACKAGES_TOKEN is set. Restore will use any credentials already
configured for the github-honua source. For a fresh checkout, set
HONUA_GITHUB_PACKAGES_USER and HONUA_GITHUB_PACKAGES_TOKEN with read:packages.
EOF
  else
    step "Using temporary GitHub Packages NuGet credentials"
  fi

  step "Restore solution"
  if ! run dotnet restore "${solution}" --configfile "${restore_config}"; then
    cat >&2 <<'EOF'
error: dotnet restore failed.

If the failure is NU1301/401/403 for https://nuget.pkg.github.com/honua-io,
refresh the GitHub Packages token and rerun:

  export HONUA_GITHUB_PACKAGES_USER=<github-user-or-bot>
  export HONUA_GITHUB_PACKAGES_TOKEN=<token-with-read:packages>
  scripts/validate-local.sh
EOF
    exit 1
  fi

  step "Build solution"
  run dotnet build "${solution}" --no-restore --configuration "${configuration}"

  step "Test solution"
  run dotnet test "${solution}" --no-build --no-restore --configuration "${configuration}" --logger "console;verbosity=minimal"

  step "Run smoke tests"
  run dotnet test tests/Honua.Mobile.Smoke.Tests/Honua.Mobile.Smoke.Tests.csproj \
    --no-build --no-restore --configuration "${configuration}" \
    --logger "console;verbosity=minimal"

  if [[ "${run_format}" == "true" ]]; then
    step "Verify dotnet format"
    run dotnet format src/Honua.Mobile.Sdk/Honua.Mobile.Sdk.csproj --no-restore --verify-no-changes --verbosity minimal
    run dotnet format src/Honua.Mobile.Offline/Honua.Mobile.Offline.csproj --no-restore --verify-no-changes --verbosity minimal
    run dotnet format src/Honua.Mobile.Field/Honua.Mobile.Field.csproj --no-restore --verify-no-changes --verbosity minimal
  fi
fi

if [[ "${run_npm}" == "true" ]]; then
  if [[ "${run_npm_ci}" == "true" ]]; then
    step "Install embed dependencies"
    run npm ci --prefix src/Honua.Embed
  fi

  step "Build embed package"
  run npm run build --prefix src/Honua.Embed

  step "Test embed package"
  run npm test --prefix src/Honua.Embed
fi

step "Local validation completed"
