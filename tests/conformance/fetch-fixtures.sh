#!/usr/bin/env bash
#
# Fetch a *pinned* conformance fixture version from the geospatial-grpc GitHub
# Release and verify its integrity. This is the helper downstream SDK CI jobs
# (honua-sdk-dotnet / honua-sdk-js / honua-sdk-python / honua-mobile) use to
# pull the same canonical fixtures rather than copying files from the repo tree.
#
# It downloads the release asset
#   conformance-fixtures-<version>.tar.gz
# (and its .sha256), verifies the checksum, extracts it, and re-verifies every
# packaged file against the in-tarball SHA256SUMS. The result is a directory
# containing fixtures/, golden/, run.sh, manifest, and VERSION for that exact
# schema release.
#
# Usage:
#   conformance/fetch-fixtures.sh --version 0.1.0-alpha.1 [--dest DIR] [--repo OWNER/REPO]
#
# Pin a specific version (never "latest") so CI is deterministic. The version
# string maps 1:1 to a geospatial-grpc release tag (see conformance/README.md
# and VERSIONING.md).
#
# Download method (auto-detected, in order):
#   1. `gh release download` if the GitHub CLI is available and authenticated;
#   2. plain `curl`/`wget` against the public release-download URL otherwise.
#
# Requirements: bash, tar, sha256sum, and one of (gh | curl | wget).

set -euo pipefail

VERSION=""
DEST=""
REPO="honua-io/geospatial-grpc"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="${2:-}"; shift 2 ;;
    --dest)    DEST="${2:-}"; shift 2 ;;
    --repo)    REPO="${2:-}"; shift 2 ;;
    -h|--help)
      sed -n '2,30p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *)
      echo "error: unknown argument: $1" >&2
      echo "usage: $0 --version X.Y.Z [--dest DIR] [--repo OWNER/REPO]" >&2
      exit 2
      ;;
  esac
done

if [[ -z "${VERSION}" ]]; then
  echo "error: --version is required (pin an exact version, not 'latest')" >&2
  exit 2
fi

for tool in tar sha256sum; do
  if ! command -v "${tool}" >/dev/null 2>&1; then
    echo "error: ${tool} not found on PATH" >&2
    exit 1
  fi
done

PKG_NAME="conformance-fixtures-${VERSION}"
TARBALL="${PKG_NAME}.tar.gz"
TAG="v${VERSION}"
DEST="${DEST:-${PKG_NAME}}"

WORK="$(mktemp -d)"
trap 'rm -rf "${WORK}"' EXIT

download_with_gh() {
  command -v gh >/dev/null 2>&1 || return 1
  gh release download "${TAG}" \
    --repo "${REPO}" \
    --pattern "${TARBALL}" \
    --pattern "${TARBALL}.sha256" \
    --dir "${WORK}" 2>/dev/null
}

download_with_http() {
  local base="https://github.com/${REPO}/releases/download/${TAG}"
  local f
  for f in "${TARBALL}" "${TARBALL}.sha256"; do
    if command -v curl >/dev/null 2>&1; then
      curl -fsSL -o "${WORK}/${f}" "${base}/${f}" || return 1
    elif command -v wget >/dev/null 2>&1; then
      wget -q -O "${WORK}/${f}" "${base}/${f}" || return 1
    else
      echo "error: need gh, curl, or wget to download" >&2
      return 1
    fi
  done
}

echo "Fetching ${TARBALL} from ${REPO}@${TAG} ..."
if ! download_with_gh; then
  download_with_http
fi

if [[ ! -f "${WORK}/${TARBALL}" ]]; then
  echo "error: failed to download ${TARBALL} for ${TAG} from ${REPO}" >&2
  exit 1
fi

# Verify the tarball checksum if the .sha256 sidecar was retrieved.
if [[ -f "${WORK}/${TARBALL}.sha256" ]]; then
  echo "Verifying tarball checksum ..."
  (cd "${WORK}" && sha256sum -c "${TARBALL}.sha256")
else
  echo "warning: ${TARBALL}.sha256 not found; skipping tarball checksum verification" >&2
fi

# Extract.
mkdir -p "${WORK}/extract"
tar -C "${WORK}/extract" -xzf "${WORK}/${TARBALL}"

EXTRACTED="${WORK}/extract/${PKG_NAME}"
if [[ ! -d "${EXTRACTED}" ]]; then
  echo "error: extracted tarball missing expected dir ${PKG_NAME}/" >&2
  exit 1
fi

# Re-verify every packaged file against the in-tarball manifest.
if [[ -f "${EXTRACTED}/SHA256SUMS" ]]; then
  echo "Verifying per-file checksums ..."
  (cd "${EXTRACTED}" && sha256sum -c SHA256SUMS)
fi

# Confirm the embedded VERSION matches the requested pin.
if [[ -f "${EXTRACTED}/VERSION" ]]; then
  got="$(tr -d '[:space:]' < "${EXTRACTED}/VERSION")"
  if [[ "${got}" != "${VERSION}" ]]; then
    echo "error: tarball VERSION (${got}) does not match requested (${VERSION})" >&2
    exit 1
  fi
fi

# Publish to the destination directory.
rm -rf "${DEST}"
mkdir -p "$(dirname "${DEST}")" 2>/dev/null || true
mv "${EXTRACTED}" "${DEST}"

echo
echo "Fixtures ${VERSION} ready at: ${DEST}"
echo "  fixtures/       canonical payloads + manifest.txt"
echo "  golden/         canonical round-trip goldens"
echo "  run.sh          verification harness (buf required on PATH)"
echo "  VERSION         ${VERSION}"
