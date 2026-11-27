#!/usr/bin/env bash
set -euo pipefail

feed="${1:?Provide an empty feed directory}"
repo=honua-io/honua-sdk-dotnet
run=45962153231
artifact=15182046058
revision=aa9c90c9c6dc355e09a6e0215e51e698f9cfe7c8
digest=77f78e7219a60e5669172e2e2db1f37a24ee0bc40807ae38efc76b68135d5650

test "$(gh api "repos/$repo/actions/runs/$run" --jq '.head_sha + ":" + .conclusion')" = "$revision:success"
test "$(gh api "repos/$repo/actions/artifacts/$artifact" --jq '(.workflow_run.id | tostring) + ":" + .digest')" = "$run:sha256:$digest"
mkdir -p "$feed"
archive="$(mktemp)"
trap 'rm -f "$archive"' EXIT
gh api "repos/$repo/actions/artifacts/$artifact/zip" > "$archive"
printf '%s  %s\n' "$digest" "$archive" | sha256sum -c
unzip -j "$archive" 'Honua.Sdk.Field.5.1.1-fieldtasks.3.nupkg' 'Honua.Sdk.Abstractions.5.1.1-fieldtasks.3.nupkg' -d "$feed"
