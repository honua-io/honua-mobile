#!/usr/bin/env bash
set -euo pipefail

feed="${1:?Provide an empty feed directory}"
repo=honua-io/honua-sdk-dotnet
run=45973639431
artifact=15186337359
revision=b8427e9213a214e532daad9fa0052ba3fcc313f3
digest=9bd9025ade1c487a7c654f7c063da2225c2ece8beb9e9859a4447cff4c576a59

test "$(gh api "repos/$repo/actions/runs/$run" --jq '.head_sha + ":" + .conclusion')" = "$revision:success"
test "$(gh api "repos/$repo/actions/artifacts/$artifact" --jq '(.workflow_run.id | tostring) + ":" + .digest')" = "$run:sha256:$digest"
mkdir -p "$feed"
if [[ -n "$(find "$feed" -mindepth 1 -maxdepth 1 -print -quit)" ]]; then
  echo "SDK candidate feed directory must be empty" >&2
  exit 1
fi
archive="$(mktemp)"
trap 'rm -f "$archive"' EXIT
gh api "repos/$repo/actions/artifacts/$artifact/zip" > "$archive"
printf '%s  %s\n' "$digest" "$archive" | sha256sum -c
unzip -q "$archive" -d "$feed"
(
  cd "$feed"
  sha256sum --check SHA256SUMS
  test -s Honua.Sdk.Field.5.1.1-fieldcombined.1.nupkg
  test -s Honua.Sdk.Abstractions.5.1.1-fieldcombined.1.nupkg
)
