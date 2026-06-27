#!/usr/bin/env bash
#
# Enforces a single Apache-2.0 license-header policy across the shipped library
# sources (src/Honua.Mobile.Sdk, src/Honua.Mobile.Offline, src/Honua.Mobile.Maui).
# Every tracked C# file in those projects must begin with the two-line header.
#
# Usage: scripts/check-license-headers.sh
# Exit code 1 (and a list of offenders) when a file is missing the header.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

expected_first='// Copyright (c) Honua, Inc. and contributors.'
expected_second='// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.'

missing=()
while IFS= read -r file; do
  first="$(sed -n '1p' "${file}")"
  second="$(sed -n '2p' "${file}")"
  if [[ "${first}" != "${expected_first}" || "${second}" != "${expected_second}" ]]; then
    missing+=("${file}")
  fi
done < <(find src/Honua.Mobile.Sdk src/Honua.Mobile.Offline src/Honua.Mobile.Maui \
  -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | sort)

if [[ "${#missing[@]}" -gt 0 ]]; then
  echo "::error::The following shipped-library sources are missing the Apache-2.0 license header:"
  printf '  %s\n' "${missing[@]}"
  echo ""
  echo "Prepend this header (followed by a blank line):"
  echo "  ${expected_first}"
  echo "  ${expected_second}"
  exit 1
fi

echo "All shipped-library sources carry the Apache-2.0 license header."
