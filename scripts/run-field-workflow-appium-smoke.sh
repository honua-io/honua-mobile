#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACT_DIR="${HONUA_MOBILE_APPIUM_ARTIFACT_DIR:-${ROOT_DIR}/TestResults/appium-field-workflow}"
RESULT_FILE="${ARTIFACT_DIR}/honua-mobile-field-workflow-appium-smoke-result.json"
REQUIRED_STEPS='["launch","configure-server","download-project","create-record","sync"]'

mkdir -p "${ARTIFACT_DIR}"

json_array() {
  python3 - "$@" <<'PY'
import json
import sys

print(json.dumps(sys.argv[1:]))
PY
}

write_result() {
  local status="$1"
  local reason="$2"
  local missing_json="$3"

  python3 - "${RESULT_FILE}" "${status}" "${reason}" "${missing_json}" "${REQUIRED_STEPS}" <<'PY'
import json
import sys

result_file, status, reason, missing_json, steps_json = sys.argv[1:]
payload = {
    "schemaVersion": "honua.mobile.field-workflow-appium-smoke.v1",
    "status": status,
    "reason": reason,
    "requiredSteps": json.loads(steps_json),
    "missingConfiguration": json.loads(missing_json),
}
with open(result_file, "w", encoding="utf-8") as handle:
    json.dump(payload, handle, indent=2)
    handle.write("\n")
PY
}

is_truthy() {
  case "${1:-}" in
    1|true|TRUE|yes|YES) return 0 ;;
    *) return 1 ;;
  esac
}

if ! is_truthy "${HONUA_MOBILE_APPIUM_SMOKE:-}"; then
  write_result \
    "skipped" \
    "Set HONUA_MOBILE_APPIUM_SMOKE=1 to run the gated Appium field workflow smoke." \
    "[]"
  echo "::notice::Skipping Appium field workflow smoke; HONUA_MOBILE_APPIUM_SMOKE is not enabled."
  exit 0
fi

missing=()
for name in \
  HONUA_MOBILE_APPIUM_SERVER_URL \
  HONUA_MOBILE_FIELD_APP_PATH \
  HONUA_MOBILE_SMOKE_BASE_URL \
  HONUA_MOBILE_SMOKE_SERVICE_ID \
  HONUA_MOBILE_SMOKE_LAYER_ID
do
  if [[ -z "${!name:-}" ]]; then
    missing+=("${name}")
  fi
done

if [[ ${#missing[@]} -gt 0 ]]; then
  missing_json="$(json_array "${missing[@]}")"
  write_result \
    "skipped" \
    "Appium field workflow smoke is gated until device, app, and server settings are configured." \
    "${missing_json}"
  echo "::notice::Skipping Appium field workflow smoke; missing configuration: ${missing[*]}."
  exit 0
fi

if [[ -z "${HONUA_MOBILE_APPIUM_COMMAND:-}" ]]; then
  write_result \
    "skipped" \
    "HONUA_MOBILE_APPIUM_COMMAND must point to the repository or lab runner that drives launch, server configuration, project download, record creation, and sync." \
    "[]"
  echo "::notice::Skipping Appium field workflow smoke; HONUA_MOBILE_APPIUM_COMMAND is not configured."
  exit 0
fi

set +e
bash -lc "${HONUA_MOBILE_APPIUM_COMMAND}"
exit_code=$?
set -e

if [[ ${exit_code} -eq 0 ]]; then
  write_result "passed" "Configured Appium field workflow smoke completed." "[]"
  exit 0
fi

write_result "failed" "Configured Appium field workflow smoke failed with exit code ${exit_code}." "[]"
exit "${exit_code}"
