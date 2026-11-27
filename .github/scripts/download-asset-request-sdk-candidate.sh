#!/usr/bin/env bash
set -euo pipefail

feed="${1:?Provide an empty feed directory}"
repo=honua-io/honua-sdk-dotnet
run=46061364907
artifact=15229262279
revision=9ead0bfe39dff94cadb0e1aef00071a820c84511
digest=1f89e1c0ac9c5ef64d739476a40c21670da3d49a9202285020fe07e494d1f46c

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
3e010c44c17d2ddc6ad47c27a154ad8628707f7717b99e82ec33bc46e0e1e20a  Honua.Sdk.Field.5.1.1-fieldcombined.4.nupkg
bc1e4f74f13caa92241bbc15355f924ca7cd74ce89f4830aa4edd18b68d325ca  Honua.Sdk.Abstractions.5.1.1-fieldcombined.4.nupkg
CHECKSUMS
)
printf 'sdk-source=%s\nworkflow-run=%s\nartifact-id=%s\narchive-sha256=%s\n' \
  "$revision" "$run" "$artifact" "$digest"
