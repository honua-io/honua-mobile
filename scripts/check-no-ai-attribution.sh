#!/usr/bin/env bash
#
# Enforces the AGENTS.md "no agent attribution" policy: commit messages and the
# PR body must not carry AI/agent co-author trailers or "generated with" lines.
# See AGENTS.md ("Commit hygiene — no agent attribution").
#
# Usage:
#   scripts/check-no-ai-attribution.sh [<git-range>]
#
# - <git-range> defaults to "origin/HEAD..HEAD" (falls back to the last commit
#   when no range can be resolved). Every commit message in the range is checked.
# - If the PR_BODY environment variable is set, its contents are checked too.
#
# Exit code 1 (and a report) when a forbidden marker is found; 0 otherwise.
set -euo pipefail

# Case-insensitive markers that must never appear in commit messages or PR bodies.
# Tuned to catch agent attribution without flagging legitimate human co-authors.
forbidden_regex='co-authored-by:[[:space:]]*.*(claude|anthropic|codex|copilot|chatgpt|openai|gemini|gpt-[0-9])|noreply@anthropic\.com|generated with[[:space:]]*(\[)?(claude|codex|github copilot)|🤖'

range="${1:-}"
if [[ -z "${range}" ]]; then
  if git rev-parse --verify --quiet origin/HEAD >/dev/null; then
    range="origin/HEAD..HEAD"
  else
    range="HEAD~1..HEAD"
  fi
fi

violations=0

check_text() {
  local label="$1"
  local text="$2"
  if printf '%s' "${text}" | grep -iEn "${forbidden_regex}" >/dev/null 2>&1; then
    echo "::error::${label} contains forbidden AI/agent attribution:"
    printf '%s\n' "${text}" | grep -iEn "${forbidden_regex}" || true
    violations=$((violations + 1))
  fi
}

# Commit messages in the range.
if commits="$(git rev-list "${range}" 2>/dev/null)"; then
  for sha in ${commits}; do
    check_text "commit ${sha}" "$(git log -1 --format='%B' "${sha}")"
  done
else
  echo "warning: could not resolve git range '${range}'; skipping commit scan" >&2
fi

# PR body, when provided by CI.
if [[ -n "${PR_BODY:-}" ]]; then
  check_text "PR body" "${PR_BODY}"
fi

if [[ "${violations}" -gt 0 ]]; then
  echo ""
  echo "Found ${violations} attribution violation(s). Remove AI/agent co-author trailers," >&2
  echo "'Generated with ...' lines, and 🤖 markers (see AGENTS.md), then re-commit." >&2
  exit 1
fi

echo "No AI/agent attribution found."
