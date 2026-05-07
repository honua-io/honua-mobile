#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
manifest="${repo_root}/quality/android-store-prereqs.json"
store_doc="${repo_root}/docs/guides/mobile-store-prereqs.md"
distribution_doc="${repo_root}/docs/guides/mobile-android-internal-distribution.md"
workflow="${repo_root}/.github/workflows/android-internal-distribution.yml"

fail() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

require_file() {
  local path="$1"
  [[ -f "${path}" ]] || fail "Missing required file: ${path#${repo_root}/}"
}

require_text() {
  local needle="$1"
  local path="$2"
  grep -Fq "${needle}" "${path}" || fail "${path#${repo_root}/} does not mention '${needle}'"
}

require_file "${manifest}"
require_file "${store_doc}"
require_file "${distribution_doc}"
require_file "${workflow}"

command -v jq >/dev/null 2>&1 || fail "jq is required to validate ${manifest#${repo_root}/}"

schema="$(jq -r '.schema' "${manifest}")"
[[ "${schema}" == "honua.mobile.android-store-prereqs.v1" ]] || fail "Unexpected manifest schema '${schema}'"

issue="$(jq -r '.githubIssue' "${manifest}")"
[[ "${issue}" == "85" ]] || fail "Manifest githubIssue must be 85"

service_account_secret="$(jq -r '.playServiceAccountSecret' "${manifest}")"
[[ "${service_account_secret}" == "GOOGLE_PLAY_SERVICE_ACCOUNT_JSON" ]] || fail "Unexpected Play service account secret '${service_account_secret}'"
require_text "${service_account_secret}" "${store_doc}"
require_text "${service_account_secret}" "${distribution_doc}"
require_text "${service_account_secret}" "${workflow}"

mapfile -t environment_names < <(jq -r '.githubEnvironments[].name' "${manifest}")
[[ "${#environment_names[@]}" -gt 0 ]] || fail "Manifest must define at least one GitHub environment"

for environment_name in "${environment_names[@]}"; do
  require_text "${environment_name}" "${store_doc}"
done

target_count="$(jq '.appTargets | length' "${manifest}")"
[[ "${target_count}" -gt 0 ]] || fail "Manifest must define at least one app target"

for index in $(seq 0 "$((target_count - 1))"); do
  target_id="$(jq -r ".appTargets[${index}].id" "${manifest}")"
  project_rel="$(jq -r ".appTargets[${index}].project" "${manifest}")"
  package_id="$(jq -r ".appTargets[${index}].androidPackageId" "${manifest}")"
  secret_stem="$(jq -r ".appTargets[${index}].signingSecretStem" "${manifest}")"
  project="${repo_root}/${project_rel}"

  [[ "${target_id}" != "null" && -n "${target_id}" ]] || fail "App target ${index} is missing id"
  [[ "${project_rel}" != "null" && -n "${project_rel}" ]] || fail "App target ${target_id} is missing project"
  [[ "${package_id}" =~ ^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ ]] || fail "App target ${target_id} has invalid Android package ID '${package_id}'"
  [[ "${secret_stem}" =~ ^ANDROID_[A-Z0-9_]+_UPLOAD$ ]] || fail "App target ${target_id} has invalid signing secret stem '${secret_stem}'"
  require_file "${project}"

  application_id="$(
    sed -n 's:.*<ApplicationId>\([^<][^<]*\)</ApplicationId>.*:\1:p' "${project}" |
      head -n 1 |
      tr -d '[:space:]'
  )"
  [[ "${application_id}" == "${package_id}" ]] || fail "${project_rel} ApplicationId '${application_id}' does not match manifest package '${package_id}'"

  require_text "${target_id}" "${workflow}"
  require_text "${project_rel}" "${workflow}"
  require_text "${package_id}" "${workflow}"
  require_text "${secret_stem}" "${workflow}"

  require_text "${project_rel}" "${store_doc}"
  require_text "${package_id}" "${store_doc}"
  require_text "${package_id}" "${distribution_doc}"
  require_text "${secret_stem}" "${distribution_doc}"

  mapfile -t required_secrets < <(jq -r ".appTargets[${index}].requiredSigningSecrets[]" "${manifest}")
  [[ "${#required_secrets[@]}" -eq 4 ]] || fail "App target ${target_id} must define four required signing secrets"

  for secret_name in "${required_secrets[@]}"; do
    [[ "${secret_name}" == "${secret_stem}_"* ]] || fail "Secret '${secret_name}' does not match stem '${secret_stem}'"
    require_text "${secret_name}" "${store_doc}"
    require_text "${secret_name}" "${distribution_doc}"
  done

  mapfile -t rotation_secrets < <(jq -r ".appTargets[${index}].rotationScope[]" "${manifest}")
  [[ "${required_secrets[*]}" == "${rotation_secrets[*]}" ]] || fail "App target ${target_id} rotation scope must match required signing secrets"
done

printf 'Android store prerequisite manifest is in sync with project IDs, docs, and workflow mapping.\n'
