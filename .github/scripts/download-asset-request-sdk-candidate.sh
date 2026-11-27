#!/usr/bin/env bash
set -euo pipefail

feed="${1:?Provide an empty feed directory}"
repo=honua-io/honua-sdk-dotnet
run=46060511147
artifact=15229062590
revision=01542f39ebbd164a8f904a4eaf1a3987169a5c74
digest=0c12abb42c10c005d3af805cbc90dc18e1ddb539f163b9f12a881066bbba17dc

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
  sha256sum --check <<'CHECKSUMS'
15f300d1a31a2caa8f13b591fd004bf62deb36363b65b1d08d364967b8b0add2  Honua.Sdk.Field.5.1.1-fieldasset.3.nupkg
7546404ede4505c58ad8e4dd542de49b43b12827499ee30fa6f4da6e2e978995  Honua.Sdk.Abstractions.5.1.1-fieldasset.3.nupkg
CHECKSUMS
)
printf 'sdk-source=%s\nworkflow-run=%s\nartifact-id=%s\narchive-sha256=%s\n' \
  "$revision" "$run" "$artifact" "$digest"
