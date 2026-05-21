#!/usr/bin/env bash
# Sync the vendored mobile-offline-demo-v1.sql seed against the upstream copy
# in honua-io/honua-server.
#
# Default behaviour: fetch the upstream version into a temp file, diff it
# against the vendored copy, and exit non-zero if they differ. This surfaces
# drift without ever modifying the working tree.
#
# Pass --write to overwrite the vendored seed and refresh tests/seed/UPSTREAM.md
# with the new upstream blob SHA + commit + timestamp.
#
# Requires `gh` (authenticated against github.com with read scope on
# honua-io/honua-server) and standard POSIX tools.
set -euo pipefail

REPO="honua-io/honua-server"
UPSTREAM_PATH="tests/seed/mobile-offline-demo-v1.sql"
VENDORED_PATH="tests/seed/mobile-offline-demo-v1.sql"
UPSTREAM_DOC="tests/seed/UPSTREAM.md"
DEFAULT_REF=""

WRITE=0
for arg in "$@"; do
  case "$arg" in
    --write) WRITE=1 ;;
    --ref=*) DEFAULT_REF="${arg#--ref=}" ;;
    -h|--help)
      sed -n '2,12p' "$0"
      exit 0
      ;;
    *)
      echo "error: unknown argument: $arg" >&2
      exit 2
      ;;
  esac
done

# Resolve repo root (this script lives in tools/).
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." >/dev/null 2>&1 && pwd)"
cd "${REPO_ROOT}"

if ! command -v gh >/dev/null 2>&1; then
  echo "error: gh CLI is required (https://cli.github.com/)" >&2
  exit 2
fi

if [[ ! -f "${VENDORED_PATH}" ]]; then
  echo "error: vendored seed not found at ${VENDORED_PATH}" >&2
  exit 2
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "${TMP_DIR}"' EXIT
UPSTREAM_TMP="${TMP_DIR}/upstream.sql"

API_PATH="repos/${REPO}/contents/${UPSTREAM_PATH}"
if [[ -n "${DEFAULT_REF}" ]]; then
  API_PATH="${API_PATH}?ref=${DEFAULT_REF}"
fi

echo "fetching upstream ${REPO}:${UPSTREAM_PATH}${DEFAULT_REF:+@${DEFAULT_REF}} ..."
gh api "${API_PATH}" -H "Accept: application/vnd.github.raw" > "${UPSTREAM_TMP}"
UPSTREAM_SHA="$(gh api "${API_PATH}" --jq .sha)"

if [[ -z "${UPSTREAM_SHA}" ]]; then
  echo "error: could not resolve upstream blob SHA" >&2
  exit 1
fi

DEFAULT_BRANCH="$(gh api "repos/${REPO}" --jq .default_branch)"
DEFAULT_BRANCH_REF="${DEFAULT_REF:-${DEFAULT_BRANCH}}"
DEFAULT_BRANCH_SHA="$(gh api "repos/${REPO}/commits/${DEFAULT_BRANCH_REF}" --jq .sha 2>/dev/null || true)"

if cmp -s "${UPSTREAM_TMP}" "${VENDORED_PATH}"; then
  echo "vendored ${VENDORED_PATH} is up to date with upstream (sha ${UPSTREAM_SHA})."
  exit 0
fi

echo
echo "drift detected between vendored ${VENDORED_PATH} and upstream:"
echo "---"
diff -u "${VENDORED_PATH}" "${UPSTREAM_TMP}" || true
echo "---"

if [[ "${WRITE}" -ne 1 ]]; then
  echo
  echo "refusing to overwrite without --write."
  echo "re-run with: tools/sync-mobile-offline-seed.sh --write"
  exit 1
fi

cp "${UPSTREAM_TMP}" "${VENDORED_PATH}"
BYTES="$(wc -c < "${VENDORED_PATH}" | tr -d ' ')"
TODAY="$(date -u +%Y-%m-%d)"

# Refresh UPSTREAM.md current-snapshot table in place.
python3 - "${UPSTREAM_DOC}" "${UPSTREAM_SHA}" "${DEFAULT_BRANCH_SHA}" "${TODAY}" "${BYTES}" <<'PY'
import sys, re, pathlib

doc_path, blob_sha, commit_sha, today, bytes_count = sys.argv[1:6]
path = pathlib.Path(doc_path)
text = path.read_text()

replacements = {
    r"(\| Upstream blob SHA \| `)[^`]+(` \|)": rf"\g<1>{blob_sha}\g<2>",
    r"(\| Upstream `trunk` commit at fetch time \| `)[^`]+(` \|)": rf"\g<1>{commit_sha}\g<2>",
    r"(\| Vendored on \| )\d{4}-\d{2}-\d{2}( \|)": rf"\g<1>{today}\g<2>",
    r"(\| Bytes \| )\d+( \|)": rf"\g<1>{bytes_count}\g<2>",
}
for pattern, repl in replacements.items():
    new_text, n = re.subn(pattern, repl, text)
    if n != 1:
        sys.stderr.write(f"warn: {pattern!r} matched {n} times in {doc_path}\n")
    text = new_text

path.write_text(text)
PY

echo
echo "wrote ${VENDORED_PATH} (${BYTES} bytes)"
echo "updated ${UPSTREAM_DOC} (blob ${UPSTREAM_SHA}, commit ${DEFAULT_BRANCH_SHA:-unknown}, vendored ${TODAY})"
echo
echo "Review and commit:"
echo "  git add ${VENDORED_PATH} ${UPSTREAM_DOC}"
