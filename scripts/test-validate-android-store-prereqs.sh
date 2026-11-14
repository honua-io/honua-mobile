#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

scripts/validate-android-store-prereqs.sh >/dev/null

temp_dir="$(mktemp -d)"
trap 'rm -rf "${temp_dir}"' EXIT

swapped_workflow="${temp_dir}/android-internal-distribution-swapped.yml"
failure_output="${temp_dir}/validation-output.txt"

sed 's@apps/Honua.Mobile.App/Honua.Mobile.App.csproj@apps/Missing/Missing.csproj@g' \
  .github/workflows/android-internal-distribution.yml > "${swapped_workflow}"

if HONUA_ANDROID_STORE_WORKFLOW="${swapped_workflow}" scripts/validate-android-store-prereqs.sh >"${failure_output}" 2>&1; then
  echo "Expected invalid Android internal distribution workflow mapping to fail validation." >&2
  exit 1
fi

grep -Fq "mobile-app" "${failure_output}"

printf 'Validated Android store prerequisite smoke tests.\n'
