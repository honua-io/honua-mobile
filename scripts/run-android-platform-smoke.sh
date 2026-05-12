#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_PATH="${ROOT_DIR}/tests/Honua.Mobile.PlatformSmoke/Honua.Mobile.PlatformSmoke.csproj"
PROJECT_DIR="$(dirname "${PROJECT_PATH}")"
PACKAGE_ID="${HONUA_MOBILE_PLATFORM_SMOKE_PACKAGE_ID:-io.honua.mobile.platformsmoke}"
CONFIGURATION="${CONFIGURATION:-Debug}"
TARGET_FRAMEWORK="net10.0-android"
ANDROID_SUPPORTED_ABIS="${ANDROID_SUPPORTED_ABIS:-x86_64}"
CONFIG_FILE="honua-mobile-platform-smoke-config.json"
RESULT_FILE="honua-mobile-platform-smoke-result.json"

require_env() {
  local name="$1"
  if [[ -z "${!name:-}" ]]; then
    echo "::error::${name} is required for Android platform smoke."
    exit 1
  fi
}

has_live_smoke_config() {
  [[ -n "${HONUA_MOBILE_SMOKE_BASE_URL:-}" ]] &&
    [[ -n "${HONUA_MOBILE_SMOKE_SERVICE_ID:-}" ]] &&
    [[ -n "${HONUA_MOBILE_SMOKE_LAYER_ID:-}" ]]
}

if ! has_live_smoke_config; then
  echo "::notice::Skipping Android live Honua platform smoke because HONUA_MOBILE_SMOKE_BASE_URL, HONUA_MOBILE_SMOKE_SERVICE_ID, or HONUA_MOBILE_SMOKE_LAYER_ID is not configured."
  exit 0
fi

require_env HONUA_MOBILE_SMOKE_BASE_URL
require_env HONUA_MOBILE_SMOKE_SERVICE_ID
require_env HONUA_MOBILE_SMOKE_LAYER_ID

adb wait-for-device
until [[ "$(adb shell getprop sys.boot_completed | tr -d '\r')" == "1" ]]; do
  sleep 1
done

dotnet restore "${PROJECT_PATH}" \
  --framework "${TARGET_FRAMEWORK}" \
  /p:AndroidSupportedAbis="${ANDROID_SUPPORTED_ABIS}"

dotnet build "${PROJECT_PATH}" \
  --configuration "${CONFIGURATION}" \
  --framework "${TARGET_FRAMEWORK}" \
  --no-restore \
  /p:AndroidSupportedAbis="${ANDROID_SUPPORTED_ABIS}" \
  /p:AndroidPackageFormat=apk \
  /p:TreatWarningsAsErrors=true

apk_path="$(find "${PROJECT_DIR}/bin/${CONFIGURATION}/${TARGET_FRAMEWORK}" -type f \( -name "*Signed.apk" -o -name "*.apk" \) | head -n 1)"
if [[ -z "${apk_path}" ]]; then
  echo "::error::Android platform smoke APK was not produced."
  exit 1
fi

adb install -r "${apk_path}" >/dev/null

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

tmp_config="$(mktemp)"
trap 'rm -f "${tmp_config}"' EXIT
printf '%s' "${config_json}" > "${tmp_config}"

adb push "${tmp_config}" "/data/local/tmp/${CONFIG_FILE}" >/dev/null
adb shell chmod 644 "/data/local/tmp/${CONFIG_FILE}" >/dev/null
adb shell run-as "${PACKAGE_ID}" sh -c "mkdir -p files && cp /data/local/tmp/${CONFIG_FILE} files/${CONFIG_FILE} && rm -f files/${RESULT_FILE}"

adb logcat -c || true
adb shell am force-stop "${PACKAGE_ID}" >/dev/null 2>&1 || true
adb shell monkey -p "${PACKAGE_ID}" -c android.intent.category.LAUNCHER 1 >/dev/null

result_json=""
deadline=$((SECONDS + 45))
while (( SECONDS < deadline )); do
  result_json="$(adb shell run-as "${PACKAGE_ID}" cat "files/${RESULT_FILE}" 2>/dev/null | tr -d '\r' || true)"
  if [[ -n "${result_json}" ]]; then
    break
  fi

  sleep 1
done

if [[ -z "${result_json}" ]]; then
  echo "::error::Android platform smoke did not write ${RESULT_FILE} before timeout."
  adb logcat -d -v time | grep -F "Honua" | tail -n 120 || true
  exit 1
fi

printf '%s\n' "${result_json}" | python3 -m json.tool
if ! printf '%s' "${result_json}" | python3 -c 'import json,sys; sys.exit(0 if json.load(sys.stdin).get("success") is True else 1)'; then
  echo "::error::Android platform smoke failed."
  exit 1
fi

elapsed_ms="$(printf '%s' "${result_json}" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("elapsedMilliseconds"))')"
echo "Android platform smoke completed in ${elapsed_ms} ms."
