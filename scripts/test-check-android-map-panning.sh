#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
export PATH="$fixture:$PATH"
export HONUA_FIELD_ANDROID_PACKAGE_ID=io.honua.collect
export HONUA_MAP_GFXINFO_PATH="$fixture/raw.txt"
export TEST_GFXINFO="$fixture/input.txt"

cat > "$fixture/adb" <<'ADB'
#!/usr/bin/env bash
case "$*" in
  get-state) echo device ;;
  'shell getprop ro.build.fingerprint') echo synthetic/fingerprint ;;
  'shell dumpsys package io.honua.collect') printf '  versionCode=1\n  versionName=1.0\n' ;;
  'shell dumpsys gfxinfo io.honua.collect reset'|'shell input swipe '*) ;;
  'shell dumpsys gfxinfo io.honua.collect') cat "$TEST_GFXINFO" ;;
  *) exit 1 ;;
esac
ADB
chmod +x "$fixture/adb"

run_gate() {
  "$root/scripts/check-android-map-panning.sh" 100 200 500 200 > "$fixture/report.json" 2> "$fixture/error.txt"
}

cat > "$TEST_GFXINFO" <<'STATS'
** Graphics info for pid 101 [io.honua.collect:auxiliary] **
Total frames rendered: 80
Janky frames: 0 (0.00%)
95th percentile: 20ms

** Graphics info for pid 202 [io.honua.collect] **
Total frames rendered: 63
Janky frames: 63 (100.00%)
95th percentile: 300ms
STATS

if run_gate; then
  echo "Healthy secondary process masked the failing main process." >&2
  exit 1
fi
grep -Fq '"panning_p95_ms": 300.0' "$fixture/report.json"
grep -Fq '"panning_jank_percent": 100.0' "$fixture/report.json"
cmp "$TEST_GFXINFO" "$HONUA_MAP_GFXINFO_PATH"

# Keep a partial main-process summary. The missing metric cannot come from a later process.
sed -i '/95th percentile: 300ms/d' "$TEST_GFXINFO"
cat >> "$TEST_GFXINFO" <<'STATS'

** Graphics info for pid 303 [io.honua.collect:other] **
95th percentile: 20ms
STATS
if run_gate; then
  echo "Accepted an incomplete main-process sample." >&2
  exit 1
fi
grep -Fq 'missing panning_p95_ms' "$fixture/error.txt"

sed -i 's/63 (100.00%)/0 (0.00%)/; /Total frames rendered: 63/a 95th percentile: 20ms' "$TEST_GFXINFO"
run_gate
grep -Fq '"frames": 63.0' "$fixture/report.json"

sed -i 's/\[io.honua.collect\]/[io.honua.collect:renamed]/' "$TEST_GFXINFO"
if run_gate; then
  echo "Accepted metrics without the main Field process." >&2
  exit 1
fi
grep -Fq 'Expected exactly one' "$fixture/error.txt"
