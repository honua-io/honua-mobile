#!/usr/bin/env bash
set -euo pipefail

feed="${1:?Provide an empty feed directory}"
repo=honua-io/honua-sdk-dotnet
run=45952807612
artifact=15177498235
revision=90ca73985560fca4b2abe486fb8841f7303bbc70
digest=65f77e12d6b8f1be62a359a97761a9f2cf2fd2ad28bc37e108f8d990d66635d3

test "$(gh api "repos/$repo/actions/runs/$run" --jq '.head_sha + ":" + .conclusion')" = "$revision:success"
test "$(gh api "repos/$repo/actions/artifacts/$artifact" --jq '(.workflow_run.id | tostring) + ":" + .digest')" = "$run:sha256:$digest"
mkdir -p "$feed"
archive="$(mktemp)"
trap 'rm -f "$archive"' EXIT
gh api "repos/$repo/actions/artifacts/$artifact/zip" > "$archive"
printf '%s  %s\n' "$digest" "$archive" | sha256sum -c
unzip -j "$archive" 'Honua.Sdk.Field.5.1.1-fieldtasks.2.nupkg' 'Honua.Sdk.Abstractions.5.1.1-fieldtasks.2.nupkg' -d "$feed"
