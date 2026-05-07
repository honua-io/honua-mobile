#!/usr/bin/env bash
set -euo pipefail

branch="${1:-${GITHUB_REF_NAME:-}}"
repo="${GITHUB_REPOSITORY:-}"

if [[ -z "${repo}" ]]; then
  remote_url="$(git config --get remote.origin.url || true)"
  if [[ "${remote_url}" =~ github.com[:/]([^/]+/[^/.]+)(\.git)?$ ]]; then
    repo="${BASH_REMATCH[1]}"
  fi
fi

if [[ -z "${repo}" ]]; then
  echo "Could not determine GitHub repository. Set GITHUB_REPOSITORY=owner/repo." >&2
  exit 2
fi

if ! command -v gh >/dev/null 2>&1; then
  echo "GitHub CLI is required. Install gh and authenticate with a token that can read branch protection." >&2
  exit 2
fi

if [[ -z "${GH_TOKEN:-${GITHUB_TOKEN:-}}" ]]; then
  echo "Set GH_TOKEN or GITHUB_TOKEN with permission to read branch protection for ${repo}." >&2
  exit 2
fi

if [[ -z "${branch}" ]]; then
  branch="$(gh repo view "${repo}" --json defaultBranchRef --jq '.defaultBranchRef.name')"
fi

protection_path="repos/${repo}/branches/${branch}/protection"

required_reviews="$(gh api "${protection_path}" --jq '.required_pull_request_reviews.required_approving_review_count // 0')"
required_status_checks_present="$(gh api "${protection_path}" --jq '(.required_status_checks != null)')"
required_status_check_count="$(gh api "${protection_path}" --jq '((.required_status_checks.contexts // []) + ((.required_status_checks.checks // []) | map(.context))) | length')"
force_pushes_enabled="$(gh api "${protection_path}" --jq '.allow_force_pushes.enabled // false')"

if (( required_reviews < 1 )); then
  echo "Branch ${branch} in ${repo} must require at least one approving review." >&2
  exit 1
fi

if [[ "${required_status_checks_present}" != "true" || "${required_status_check_count}" == "0" ]]; then
  echo "Branch ${branch} in ${repo} must require status checks." >&2
  exit 1
fi

if [[ "${force_pushes_enabled}" == "true" ]]; then
  echo "Branch ${branch} in ${repo} must disable force pushes." >&2
  exit 1
fi

echo "Branch protection OK for ${repo}:${branch}"
echo "Required approving reviews: ${required_reviews}"
echo "Required status checks: ${required_status_check_count}"
echo "Force pushes enabled: ${force_pushes_enabled}"
