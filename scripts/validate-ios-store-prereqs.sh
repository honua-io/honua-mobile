#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

failures=()

require_file() {
  local path="$1"
  if [[ ! -f "${path}" ]]; then
    failures+=("Missing required file: ${path}")
  fi
}

require_contains() {
  local path="$1"
  local expected="$2"
  local label="$3"

  if ! grep -Fq "${expected}" "${path}"; then
    failures+=("${path} does not document ${label}: ${expected}")
  fi
}

workflow_case_block() {
  local path="$1"
  local app_target="$2"

  awk -v app_target="${app_target}" '
    $0 ~ "^[[:space:]]*" app_target "\\)" {
      in_block = 1
    }

    in_block {
      print
      if ($0 ~ "^[[:space:]]*;;[[:space:]]*$") {
        exit
      }
    }
  ' "${path}"
}

require_workflow_target_mapping() {
  local path="$1"
  local app_target="$2"
  local app_name="$3"
  local project_path="$4"
  local bundle_id="$5"
  local profile_secret="$6"
  local block

  if [[ ! -f "${path}" ]]; then
    return
  fi

  if ! block="$(workflow_case_block "${path}" "${app_target}")"; then
    failures+=("${path} could not be read while validating workflow app_target mapping block for ${app_target}.")
    return
  fi

  if [[ -z "${block}" ]]; then
    failures+=("${path} does not define workflow app_target mapping block for ${app_target}.")
    return
  fi

  if ! grep -Fq "${project_path}" <<< "${block}"; then
    failures+=("${path} ${app_target} mapping does not bind ${app_name} project path: ${project_path}")
  fi

  if ! grep -Fq "${bundle_id}" <<< "${block}"; then
    failures+=("${path} ${app_target} mapping does not bind ${app_name} bundle ID: ${bundle_id}")
  fi

  if ! grep -Fq "${profile_secret}" <<< "${block}"; then
    failures+=("${path} ${app_target} mapping does not bind ${app_name} provisioning profile secret: ${profile_secret}")
  fi
}

application_id_for_project() {
  local project_path="$1"
  local application_id=""

  if command -v dotnet >/dev/null 2>&1; then
    application_id="$(dotnet msbuild "${project_path}" \
      -nologo \
      -getProperty:ApplicationId \
      -p:TargetFramework=net10.0-ios 2>/dev/null \
      | tr -d '\r' \
      | sed -n '/./p' \
      | tail -n 1 || true)"
  fi

  if [[ -z "${application_id}" ]]; then
    application_id="$(sed -n 's:.*<ApplicationId>\([^<]*\)</ApplicationId>.*:\1:p' "${project_path}" \
      | head -n 1 \
      | tr -d '[:space:]')"
  fi

  printf '%s' "${application_id}"
}

store_prereqs_doc="docs/guides/mobile-store-prereqs.md"
testflight_doc="docs/guides/mobile-testflight-builds.md"
testflight_workflow="${IOS_TESTFLIGHT_WORKFLOW_PATH:-.github/workflows/ios-testflight.yml}"
release_checklist="quality/release-checklist.md"

for path in \
  "${store_prereqs_doc}" \
  "${testflight_doc}" \
  "${testflight_workflow}" \
  "${release_checklist}"
do
  require_file "${path}"
done

target_rows=(
  "mobile-app|Honua Mobile App|apps/Honua.Mobile.App/Honua.Mobile.App.csproj|io.honua.mobile.app|IOS_APP_PROVISIONING_PROFILE_MOBILEPROVISION_BASE64"
  "field-collection|Honua Field Collection|apps/Honua.Mobile.FieldCollection/Honua.Mobile.FieldCollection.csproj|io.honua.mobile.fieldcollection|IOS_FIELD_COLLECTION_PROVISIONING_PROFILE_MOBILEPROVISION_BASE64"
)

for row in "${target_rows[@]}"; do
  IFS='|' read -r app_target app_name project_path bundle_id profile_secret <<< "${row}"

  require_file "${project_path}"

  actual_application_id="$(application_id_for_project "${project_path}")"
  if [[ "${actual_application_id}" != "${bundle_id}" ]]; then
    failures+=("${project_path} ApplicationId is '${actual_application_id:-<empty>}', expected '${bundle_id}' for ${app_target}.")
  fi

  require_contains "${store_prereqs_doc}" "${project_path}" "${app_name} project path"
  require_contains "${store_prereqs_doc}" "${bundle_id}" "${app_name} iOS bundle ID"
  require_contains "${testflight_doc}" "${project_path}" "${app_name} TestFlight project path"
  require_contains "${testflight_doc}" "${bundle_id}" "${app_name} TestFlight bundle ID"
  require_contains "${testflight_doc}" "${profile_secret}" "${app_name} provisioning profile secret"
  require_workflow_target_mapping "${testflight_workflow}" "${app_target}" "${app_name}" "${project_path}" "${bundle_id}" "${profile_secret}"
done

required_ios_secret_names=(
  APPLE_TEAM_ID
  APP_STORE_CONNECT_ISSUER_ID
  APP_STORE_CONNECT_KEY_ID
  APP_STORE_CONNECT_API_KEY_P8
  IOS_DISTRIBUTION_CERTIFICATE_P12_BASE64
  IOS_DISTRIBUTION_CERTIFICATE_PASSWORD
  IOS_APP_PROVISIONING_PROFILE_MOBILEPROVISION_BASE64
  IOS_FIELD_COLLECTION_PROVISIONING_PROFILE_MOBILEPROVISION_BASE64
)

for secret_name in "${required_ios_secret_names[@]}"; do
  require_contains "${store_prereqs_doc}" "${secret_name}" "iOS secret ${secret_name}"
  require_contains "${testflight_doc}" "${secret_name}" "TestFlight secret ${secret_name}"
  require_contains "${testflight_workflow}" "${secret_name}" "workflow secret ${secret_name}"
done

if grep -Rqs "<key>CFBundleIdentifier</key>" \
  apps/Honua.Mobile.App/Platforms/iOS \
  apps/Honua.Mobile.FieldCollection/Platforms/iOS
then
  failures+=("iOS Info.plist files declare CFBundleIdentifier; update docs and workflow mapping if the MAUI ApplicationId is no longer authoritative.")
fi

if [[ ${#failures[@]} -gt 0 ]]; then
  printf 'iOS store prerequisite validation failed:\n' >&2
  printf ' - %s\n' "${failures[@]}" >&2
  exit 1
fi

printf 'Validated iOS store prerequisite docs and workflow mapping for %s targets.\n' "${#target_rows[@]}"
