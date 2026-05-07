#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

scripts/validate-ios-store-prereqs.sh >/dev/null

temp_dir="$(mktemp -d)"
trap 'rm -rf "${temp_dir}"' EXIT

swapped_workflow="${temp_dir}/ios-testflight-swapped.yml"
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
    gsub("IOS_FIELD_COLLECTION_PROVISIONING_PROFILE_MOBILEPROVISION_BASE64", "IOS_APP_PROVISIONING_PROFILE_MOBILEPROVISION_BASE64")
  }

  block == "mobile-app" {
    gsub("apps/Honua.Mobile.App/Honua.Mobile.App.csproj", "apps/Honua.Mobile.FieldCollection/Honua.Mobile.FieldCollection.csproj")
    gsub("io.honua.mobile.app", "io.honua.mobile.fieldcollection")
    gsub("IOS_APP_PROVISIONING_PROFILE_MOBILEPROVISION_BASE64", "IOS_FIELD_COLLECTION_PROVISIONING_PROFILE_MOBILEPROVISION_BASE64")
  }

  {
    print
  }

  block != "" && /^[[:space:]]*;;[[:space:]]*$/ {
    block = ""
  }
' .github/workflows/ios-testflight.yml > "${swapped_workflow}"

if IOS_TESTFLIGHT_WORKFLOW_PATH="${swapped_workflow}" scripts/validate-ios-store-prereqs.sh >"${failure_output}" 2>&1; then
  echo "Expected swapped iOS TestFlight workflow mapping to fail validation." >&2
  exit 1
fi

grep -Fq "${swapped_workflow} field-collection mapping does not bind Honua Field Collection project path" "${failure_output}"
grep -Fq "${swapped_workflow} mobile-app mapping does not bind Honua Mobile App project path" "${failure_output}"

printf 'Validated iOS store prerequisite smoke tests.\n'
