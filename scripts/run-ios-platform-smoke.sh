#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_PATH="${ROOT_DIR}/tests/Honua.Mobile.PlatformSmoke/Honua.Mobile.PlatformSmoke.csproj"
PROJECT_DIR="$(dirname "${PROJECT_PATH}")"
BUNDLE_ID="${HONUA_MOBILE_PLATFORM_SMOKE_BUNDLE_ID:-io.honua.mobile.platformsmoke}"
CONFIGURATION="${CONFIGURATION:-Debug}"
TARGET_FRAMEWORK="net11.0-ios"
if [[ "$(uname -m)" == "arm64" ]]; then
  RUNTIME_IDENTIFIER="${RUNTIME_IDENTIFIER:-iossimulator-arm64}"
else
  RUNTIME_IDENTIFIER="${RUNTIME_IDENTIFIER:-iossimulator-x64}"
fi
CONFIG_FILE="honua-mobile-platform-smoke-config.json"
RESULT_FILE="honua-mobile-platform-smoke-result.json"
STARTUP_FILE="honua-mobile-ios-scene-activated"

has_live_smoke_config() {
  [[ -n "${HONUA_MOBILE_SMOKE_BASE_URL:-}" ]] &&
    [[ -n "${HONUA_MOBILE_SMOKE_SERVICE_ID:-}" ]] &&
    [[ -n "${HONUA_MOBILE_SMOKE_LAYER_ID:-}" ]]
}

if [[ "${1:-}" != "--verify-startup" ]] && ! has_live_smoke_config; then
  echo "::notice::Skipping iOS live Honua platform smoke because its endpoint variables are unset."
  exit 0
fi

resolve_simulator_udid() {
  if [[ -n "${IOS_SIMULATOR_UDID:-}" ]]; then
    printf '%s\n' "${IOS_SIMULATOR_UDID}"
    return
  fi

  local runtime_id
  runtime_id="$(xcrun simctl list -j runtimes available | python3 -c '
import json
import re
import sys

runtimes = [
    runtime for runtime in json.load(sys.stdin).get("runtimes", [])
    if runtime.get("isAvailable")
    and re.fullmatch(r"com\.apple\.CoreSimulator\.SimRuntime\.iOS-\d+(?:-\d+)*", runtime["identifier"])
    and int(runtime["version"].split(".")[0]) >= 27
]
if not runtimes:
    raise SystemExit("No available iOS 27 or newer simulator runtime found.")
runtime = max(runtimes, key=lambda entry: tuple(map(int, entry["version"].split("."))))
print(runtime["identifier"])
')"

  local device
  device="$(xcrun simctl list -j devices available | python3 -c '
import json
import sys

devices = [
    device for device in json.load(sys.stdin).get("devices", {}).get(sys.argv[1], [])
    if device.get("name", "").startswith("iPhone")
]
if not devices:
    raise SystemExit("No available iPhone simulator device found for " + sys.argv[1])
for device in devices:
    if device.get("state") == "Booted":
        print("booted", device["udid"])
        break
else:
    print("create", devices[0]["deviceTypeIdentifier"])
' "${runtime_id}")"

  local action device_id
  read -r action device_id <<< "${device}"
  if [[ "${action}" == "booted" ]]; then
    printf '%s\n' "${device_id}"
    return
  fi

  xcrun simctl create "Honua Platform Smoke" "${device_id}" "${runtime_id}"
}

simulator_udid="$(resolve_simulator_udid)"
xcrun simctl boot "${simulator_udid}" >/dev/null 2>&1 || true
xcrun simctl bootstatus "${simulator_udid}" -b

dotnet build "${PROJECT_PATH}" \
  --configuration "${CONFIGURATION}" \
  --framework "${TARGET_FRAMEWORK}" \
  /p:RuntimeIdentifier="${RUNTIME_IDENTIFIER}" \
  /p:_DeviceName=":v2:udid=${simulator_udid}" \
  /p:TreatWarningsAsErrors=true

app_path="$(find "${PROJECT_DIR}/bin/${CONFIGURATION}/${TARGET_FRAMEWORK}/${RUNTIME_IDENTIFIER}" -maxdepth 1 -type d -name "*.app" | head -n 1)"
if [[ -z "${app_path}" ]]; then
  echo "::error::iOS platform smoke app bundle was not produced."
  exit 1
fi

xcrun simctl uninstall "${simulator_udid}" "${BUNDLE_ID}" >/dev/null 2>&1 || true
xcrun simctl install "${simulator_udid}" "${app_path}"

data_container="$(xcrun simctl get_app_container "${simulator_udid}" "${BUNDLE_ID}" data)"
documents_dir="${data_container}/Documents"
mkdir -p "${documents_dir}"

if ! has_live_smoke_config; then
  rm -f "${documents_dir}/${STARTUP_FILE}"
  SIMCTL_CHILD_HONUA_MOBILE_IOS_SCENE_STARTUP_ONLY=1 \
    xcrun simctl launch --terminate-running-process "${simulator_udid}" "${BUNDLE_ID}"
  deadline=$((SECONDS + 45))
  while (( SECONDS < deadline )); do
    if [[ -s "${documents_dir}/${STARTUP_FILE}" ]]; then
      echo "The iOS simulator activated the MAUI window through the UIKit scene lifecycle."
      echo "::notice::Skipping the optional iOS live Honua query because its endpoint variables are unset."
      exit 0
    fi
    sleep 1
  done

  echo "::error::iOS platform smoke did not activate its MAUI window before timeout."
  xcrun simctl spawn "${simulator_udid}" log show --last 2m --style compact --predicate 'eventMessage CONTAINS "Honua"' || true
  exit 1
fi

config_json="$(python3 - <<'PY'
import json
import os

payload = {
    "baseUrl": os.environ["HONUA_MOBILE_SMOKE_BASE_URL"],
    "serviceId": os.environ["HONUA_MOBILE_SMOKE_SERVICE_ID"],
    "layerId": int(os.environ["HONUA_MOBILE_SMOKE_LAYER_ID"]),
}
api_key = os.environ.get("HONUA_MOBILE_SMOKE_API_KEY")
if api_key:
    payload["apiKey"] = api_key
print(json.dumps(payload, separators=(",", ":")))
PY
)"

printf '%s' "${config_json}" > "${documents_dir}/${CONFIG_FILE}"
rm -f "${documents_dir}/${RESULT_FILE}"

xcrun simctl launch --terminate-running-process "${simulator_udid}" "${BUNDLE_ID}" >/dev/null

result_json=""
deadline=$((SECONDS + 45))
while (( SECONDS < deadline )); do
  if [[ -f "${documents_dir}/${RESULT_FILE}" ]]; then
    result_json="$(cat "${documents_dir}/${RESULT_FILE}")"
    break
  fi

  sleep 1
done

if [[ -z "${result_json}" ]]; then
  echo "::error::iOS platform smoke did not write ${RESULT_FILE} before timeout."
  xcrun simctl spawn "${simulator_udid}" log show --last 2m --style compact --predicate 'eventMessage CONTAINS "Honua"' || true
  exit 1
fi

printf '%s\n' "${result_json}" | python3 -m json.tool
if ! printf '%s' "${result_json}" | python3 -c 'import json,sys; sys.exit(0 if json.load(sys.stdin).get("success") is True else 1)'; then
  echo "::error::iOS platform smoke failed."
  exit 1
fi

elapsed_ms="$(printf '%s' "${result_json}" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("elapsedMilliseconds"))')"
echo "iOS platform smoke completed in ${elapsed_ms} ms."
