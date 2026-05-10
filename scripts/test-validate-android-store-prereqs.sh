#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

scripts/validate-android-store-prereqs.sh >/dev/null

temp_dir="$(mktemp -d)"
trap 'rm -rf "${temp_dir}"' EXIT

swapped_workflow="${temp_dir}/android-internal-distribution-swapped.yml"
failure_output="${temp_dir}/validation-output.txt"

awk '
  /^[[:space:]]*field-collection\)/ {
    block = "field-collection"
  }

  /^[[:space:]]*mobile-app\)/ {
    block = "mobile-app"
  }

  block == "field-collection" {
    gsub("apps/Honua.Mobile.FieldCollection/Honua.Mobile.FieldCollection.csproj", "apps/Honua.Mobile.App/Honua.Mobile.App.csproj")
    gsub("io.honua.mobile.fieldcollection", "io.honua.mobile.app")
    gsub("ANDROID_FIELD_COLLECTION_UPLOAD", "ANDROID_APP_UPLOAD")
  }

  block == "mobile-app" {
    gsub("apps/Honua.Mobile.App/Honua.Mobile.App.csproj", "apps/Honua.Mobile.FieldCollection/Honua.Mobile.FieldCollection.csproj")
    gsub("io.honua.mobile.app", "io.honua.mobile.fieldcollection")
    gsub("ANDROID_APP_UPLOAD", "ANDROID_FIELD_COLLECTION_UPLOAD")
  }

  {
    print
  }

  block != "" && /^[[:space:]]*;;[[:space:]]*$/ {
    block = ""
  }
' .github/workflows/android-internal-distribution.yml > "${swapped_workflow}"

if HONUA_ANDROID_STORE_WORKFLOW="${swapped_workflow}" scripts/validate-android-store-prereqs.sh >"${failure_output}" 2>&1; then
  echo "Expected swapped Android internal distribution workflow mapping to fail validation." >&2
  exit 1
fi

grep -Fq "maps 'field-collection' without project 'apps/Honua.Mobile.FieldCollection/Honua.Mobile.FieldCollection.csproj'" "${failure_output}"

printf 'Validated Android store prerequisite smoke tests.\n'
