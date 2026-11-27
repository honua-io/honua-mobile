#!/usr/bin/env bash
set -euo pipefail

package=io.honua.mobile.app
apk=$(find apps/Honua.Mobile.App/bin/Release/net11.0-android/android-x64/publish -type f -name '*-Signed.apk' -print -quit)
test -n "$apk"
sha256sum "$apk"
adb shell getprop ro.build.version.sdk
adb install "$apk"
adb shell svc wifi disable
adb shell svc data disable
adb shell monkey -p "$package" -c android.intent.category.LAUNCHER 1

read_ui() {
    local attempt
    for attempt in {1..15}; do
        if adb shell uiautomator dump /sdcard/window.xml >/dev/null && adb pull /sdcard/window.xml /tmp/mobile-qualification-ui.xml >/dev/null && python3 - "$1" <<'PY'
import re
import sys
from xml.etree import ElementTree

expected = sys.argv[1]
for node in ElementTree.parse('/tmp/mobile-qualification-ui.xml').iter('node'):
    if node.get('text', '').casefold() == expected.casefold():
        x1, y1, x2, y2 = map(int, re.findall(r'\d+', node.get('bounds', '')))
        print((x1 + x2) // 2, (y1 + y2) // 2)
        sys.exit(0)
sys.exit(1)
PY
        then
            return 0
        fi
        sleep 1
    done
    cat /tmp/mobile-qualification-ui.xml >&2
    adb logcat -d -t 400 >&2
    return 1
}

coordinates=$(read_ui 'Show Map Area Count')
read -r x y <<< "$coordinates"
adb shell input tap "$x" "$y"
read_ui 'Map areas downloaded: 0' >/dev/null
echo 'Packaged reference app initialized and queried its GeoPackage on a disconnected Android emulator.'
