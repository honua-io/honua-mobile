#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package="${HONUA_FIELD_ANDROID_PACKAGE_ID:-io.honua.collect}"

if (( $# != 4 )); then
  echo "Usage: $0 start-x start-y end-x end-y" >&2
  echo "Open a populated map in Honua Field and choose two points inside its viewport." >&2
  exit 2
fi
for coordinate in "$@"; do
  if [[ ! "$coordinate" =~ ^[0-9]+$ ]]; then
    echo "Gesture coordinates must be nonnegative integer screen pixels." >&2
    exit 2
  fi
done
if (( 10#$1 == 10#$3 && 10#$2 == 10#$4 )); then
  echo "Gesture endpoints must differ." >&2
  exit 2
fi

adb get-state >/dev/null
adb shell dumpsys gfxinfo "$package" reset >/dev/null
for _ in {1..5}; do
  adb shell input swipe "$1" "$2" "$3" "$4" 400
  adb shell input swipe "$3" "$4" "$1" "$2" 400
done

stats="$(mktemp)"
trap 'rm -f "$stats"' EXIT
adb shell dumpsys gfxinfo "$package" > "$stats"
python3 - "$stats" "$root/quality/performance-budget.json" <<'PY'
import json
import re
import sys
from pathlib import Path

stats = Path(sys.argv[1]).read_text()
budgets = json.loads(Path(sys.argv[2]).read_text())["budgets"]
fields = {
    "frames": r"^Total frames rendered:\s*(\d+)",
    "panning_p95_ms": r"^95th percentile:\s*(\d+)ms",
    "panning_jank_percent": r"^Janky frames:\s*\d+\s*\(([\d.]+)%\)",
}
measurements = {}
for field, pattern in fields.items():
    match = re.search(pattern, stats, flags=re.MULTILINE)
    if not match:
        sys.exit(f"Android frame statistics are missing {field}. Check that Honua Field is running.")
    measurements[field] = float(match.group(1))

print(json.dumps(measurements, indent=2))
failed = measurements["frames"] < 60
if failed:
    print("Fewer than 60 frames rendered. Check that the gesture pans the populated map.", file=sys.stderr)
for field in ("panning_p95_ms", "panning_jank_percent"):
    value = measurements[field]
    budget = budgets[f"android_map_{field}"]
    if value > budget["error"]:
        print(f"{field}: {value:g} exceeded the {budget['error']:g} limit.", file=sys.stderr)
        failed = True
    elif value > budget["warning"]:
        print(f"{field}: {value:g} exceeded the {budget['warning']:g} warning threshold.", file=sys.stderr)
sys.exit(1 if failed else 0)
PY
