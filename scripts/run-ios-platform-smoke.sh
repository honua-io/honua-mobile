#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_PATH="${ROOT_DIR}/tests/Honua.Mobile.PlatformSmoke/Honua.Mobile.PlatformSmoke.csproj"
PROJECT_DIR="$(dirname "${PROJECT_PATH}")"
BUNDLE_ID="${HONUA_MOBILE_PLATFORM_SMOKE_BUNDLE_ID:-io.honua.mobile.platformsmoke}"
CONFIGURATION="${CONFIGURATION:-Debug}"
TARGET_FRAMEWORK="net10.0-ios"
RUNTIME_IDENTIFIER="${RUNTIME_IDENTIFIER:-iossimulator-x64}"
CONFIG_FILE="honua-mobile-platform-smoke-config.json"
RESULT_FILE="honua-mobile-platform-smoke-result.json"

require_env() {
  local name="$1"
  if [[ -z "${!name:-}" ]]; then
    echo "::error::${name} is required for iOS platform smoke."
    exit 1
  fi
}

require_env HONUA_MOBILE_SMOKE_BASE_URL
require_env HONUA_MOBILE_SMOKE_SERVICE_ID
require_env HONUA_MOBILE_SMOKE_LAYER_ID

resolve_simulator_udid() {
  if [[ -n "${IOS_SIMULATOR_UDID:-}" ]]; then
    printf '%s\n' "${IOS_SIMULATOR_UDID}"
    return
  fi

  local booted_udid
  booted_udid="$(xcrun simctl list -j devices booted | python3 -c '
import json
import sys

devices = json.load(sys.stdin).get("devices", {})
for runtime_devices in devices.values():
    for device in runtime_devices:
        if device.get("state") == "Booted":
            print(device["udid"])
            raise SystemExit
')"

  if [[ -n "${booted_udid}" ]]; then
    printf '%s\n' "${booted_udid}"
    return
  fi

  local runtime_id
  runtime_id="$(xcrun simctl list -j runtimes available | python3 -c '
import json
import sys

runtimes = [
    runtime for runtime in json.load(sys.stdin).get("runtimes", [])
    if runtime.get("isAvailable")
    and (runtime.get("platform") == "iOS" or ".iOS-" in runtime.get("identifier", ""))
]
if not runtimes:
    raise SystemExit("No available iOS simulator runtime found.")
runtimes.sort(key=lambda runtime: runtime.get("version", ""), reverse=True)
print(runtimes[0]["identifier"])
')"

  local device_type
  device_type="$(xcrun simctl list -j devicetypes | python3 -c '
import json
import sys

device_types = json.load(sys.stdin).get("devicetypes", [])
preferred = ("iPhone 16", "iPhone 15", "iPhone 14", "iPhone 13")
for name in preferred:
    for device_type in device_types:
        if device_type.get("name") == name:
            print(device_type["identifier"])
            raise SystemExit
for device_type in device_types:
    if device_type.get("name", "").startswith("iPhone"):
        print(device_type["identifier"])
        raise SystemExit
raise SystemExit("No iPhone simulator device type found.")
')"

  xcrun simctl create "Honua Platform Smoke" "${device_type}" "${runtime_id}"
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
