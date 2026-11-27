#!/usr/bin/env bash
set -euo pipefail

feed="${1:?Provide an empty feed directory}"
repo=honua-io/honua-sdk-dotnet
run=45977902720
artifact=15188481738
revision=45fc2d434123be84202bc86d5a3b8d26ea2edf17
digest=e8198bd30155295f0ecb1b50d59ee60620823885443e4414171a4d245cc55989

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
fc424d3858ac19d515675469375a14b27d3ddfaa762f66c4df0c7343679d0707  Honua.Sdk.Field.5.1.1-fieldasset.2.nupkg
04c015a748948a68131867f9be2cd41a1c04d5cbb43cdb15d0c5277c3dc5162b  Honua.Sdk.Abstractions.5.1.1-fieldasset.2.nupkg
CHECKSUMS
)
printf 'sdk-source=%s\nworkflow-run=%s\nartifact-id=%s\narchive-sha256=%s\n' \
  "$revision" "$run" "$artifact" "$digest"
