#!/usr/bin/env bash
set -euo pipefail

apk_path="$1"
package=io.honua.mobile.app
ui_xml="$(mktemp)"
device_ui_xml=/sdcard/honua-mobile-packaged-smoke.xml

cleanup() {
    local status=$?
    rm -f "$ui_xml"
    if (( status != 0 )); then
        adb logcat -d -b crash >&2 || true
    fi
}
trap cleanup EXIT

sha256sum "$apk_path"
adb install "$apk_path"
adb logcat -c
adb shell input keyevent 82

activity="$(adb shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER "$package" | tr -d '\r' | tail -n1)"
if [[ "$activity" != "$package/"* ]]; then
    echo "::error::Could not resolve the reference APK's launcher activity."
    exit 1
fi
adb shell am start -W -n "$activity"

find_text() {
    local expected="$1"
    local deadline=$((SECONDS + 45))
    while (( SECONDS < deadline )); do
        if adb shell rm -f "$device_ui_xml" &&
            adb shell uiautomator dump "$device_ui_xml" >/dev/null &&
            adb exec-out cat "$device_ui_xml" >"$ui_xml" &&
            python3 - "$ui_xml" "$package" "$expected" <<'PY'
import re
import sys
from xml.etree import ElementTree

path, package, expected = sys.argv[1:]
for node in ElementTree.parse(path).iter("node"):
    if node.get("package") == package and node.get("text", "").casefold() == expected.casefold():
        x1, y1, x2, y2 = map(int, re.findall(r"\d+", node.attrib["bounds"]))
        print((x1 + x2) // 2, (y1 + y2) // 2)
        sys.exit(0)
sys.exit(1)
PY
        then
            return 0
        fi
        sleep 1
    done
    echo "::error::The packaged reference app did not display '$expected'." >&2
    return 1
}

coordinates="$(find_text 'Show Map Area Count')"
read -r x y <<< "$coordinates"
adb shell input tap "$x" "$y"
find_text 'Map areas downloaded: 0' >/dev/null
echo 'The packaged reference APK initialized SQLite and queried its local GeoPackage.'
