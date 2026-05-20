#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage: validate-android-store-prereqs.sh --project <path> [options]

Validates the repo-side Google Play prerequisites for an Android store or
internal distribution workflow. Secrets are read from the current environment.

Options:
  --project <path>        MAUI app project to validate.
  --package-name <name>   Expected Android package name. Defaults to
                          ANDROID_PACKAGE_NAME.
  --channel <name>        Distribution channel: internal-testing or
                          internal-app-sharing.
  --artifact-type <type>  Android artifact type: aab or apk.
  -h, --help              Show this help.
USAGE
}

fail() {
  printf '::error::%s\n' "$*" >&2
  exit 1
}

project_path=""
package_name="${ANDROID_PACKAGE_NAME:-}"
channel="${ANDROID_DISTRIBUTION_CHANNEL:-internal-testing}"
artifact_type="${ANDROID_ARTIFACT_TYPE:-aab}"

while (($# > 0)); do
  case "$1" in
    --project)
      [[ $# -ge 2 ]] || fail "--project requires a value."
      project_path="$2"
      shift 2
      ;;
    --package-name)
      [[ $# -ge 2 ]] || fail "--package-name requires a value."
      package_name="$2"
      shift 2
      ;;
    --channel)
      [[ $# -ge 2 ]] || fail "--channel requires a value."
      channel="$2"
      shift 2
      ;;
    --artifact-type)
      [[ $# -ge 2 ]] || fail "--artifact-type requires a value."
      artifact_type="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      fail "Unknown argument: $1"
      ;;
  esac
done

[[ -n "${project_path}" ]] || fail "--project is required."
[[ -f "${project_path}" ]] || fail "Project file does not exist: ${project_path}"

case "${channel}" in
  internal-testing|internal-app-sharing) ;;
  *) fail "Unsupported Android distribution channel: ${channel}" ;;
esac

case "${artifact_type}" in
  aab|apk) ;;
  *) fail "Unsupported Android artifact type: ${artifact_type}" ;;
esac

required_secret_names=(
  ANDROID_KEYSTORE_BASE64
  ANDROID_KEYSTORE_PASSWORD
  ANDROID_KEY_ALIAS
  ANDROID_KEY_PASSWORD
  GOOGLE_PLAY_SERVICE_ACCOUNT_JSON
)

missing=()
for name in "${required_secret_names[@]}"; do
  if [[ -z "${!name:-}" ]]; then
    missing+=("${name}")
  fi
done

if [[ -z "${package_name}" ]]; then
  missing+=("ANDROID_PACKAGE_NAME")
fi

if ((${#missing[@]} > 0)); then
  fail "Missing required protected-environment secret(s): ${missing[*]}"
fi

if [[ ! "${package_name}" =~ ^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$ ]]; then
  fail "ANDROID_PACKAGE_NAME is not a valid final Android package ID: ${package_name}"
fi

application_id="$(
  python3 - "${project_path}" <<'PY'
import sys
import xml.etree.ElementTree as ET

path = sys.argv[1]
try:
    root = ET.parse(path).getroot()
except ET.ParseError as exc:
    print(f"XML parse error: {exc}", file=sys.stderr)
    sys.exit(2)

for element in root.iter():
    tag = element.tag.rsplit("}", 1)[-1]
    if tag == "ApplicationId" and element.text and element.text.strip():
        print(element.text.strip())
        sys.exit(0)

sys.exit(1)
PY
)" || fail "Could not read <ApplicationId> from ${project_path}"

if [[ "${application_id}" != "${package_name}" ]]; then
  fail "ANDROID_PACKAGE_NAME (${package_name}) does not match ${project_path} <ApplicationId> (${application_id})."
fi

python3 - <<'PY' || fail "GOOGLE_PLAY_SERVICE_ACCOUNT_JSON is not a valid Google service account JSON payload."
import json
import os
import sys

try:
    payload = json.loads(os.environ["GOOGLE_PLAY_SERVICE_ACCOUNT_JSON"])
except json.JSONDecodeError as exc:
    print(f"JSON parse error: {exc}", file=sys.stderr)
    sys.exit(1)

required = ("type", "client_email", "private_key", "token_uri")
missing = [name for name in required if not str(payload.get(name, "")).strip()]
if missing:
    print(f"Missing required field(s): {', '.join(missing)}", file=sys.stderr)
    sys.exit(1)

if payload.get("type") != "service_account":
    print("The 'type' field must be 'service_account'.", file=sys.stderr)
    sys.exit(1)
PY

keystore_probe="$(mktemp)"
cleanup() {
  rm -f "${keystore_probe}"
}
trap cleanup EXIT

if ! printf '%s' "${ANDROID_KEYSTORE_BASE64}" | base64 --decode > "${keystore_probe}" 2>/dev/null; then
  fail "ANDROID_KEYSTORE_BASE64 is not valid base64."
fi

if [[ ! -s "${keystore_probe}" ]]; then
  fail "ANDROID_KEYSTORE_BASE64 decoded to an empty file."
fi

for name in ANDROID_KEYSTORE_PASSWORD ANDROID_KEY_ALIAS ANDROID_KEY_PASSWORD; do
  value="${!name}"
  if [[ -z "${value//[[:space:]]/}" ]]; then
    fail "${name} cannot be blank or whitespace."
  fi
done

echo "Android store prerequisites validated for ${application_id}."
echo "Project: ${project_path}"
echo "Channel: ${channel}"
echo "Artifact type: ${artifact_type}"

if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
  {
    echo "## Android store preflight"
    echo
    echo "| Field | Value |"
    echo "| --- | --- |"
    echo "| Project | \`${project_path}\` |"
    echo "| Package ID | \`${application_id}\` |"
    echo "| Channel | \`${channel}\` |"
    echo "| Artifact type | \`${artifact_type}\` |"
    echo "| Google Play service account JSON | validated shape only |"
    echo "| Android keystore secret | base64 decoded to a non-empty file |"
  } >> "${GITHUB_STEP_SUMMARY}"
fi
