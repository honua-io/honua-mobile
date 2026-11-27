#!/usr/bin/env bash
# Exercise the adapter through the NuGet graph consumed by Field.
set -euo pipefail

if [[ $# != 4 ]]; then
    echo "Usage: $0 <mobile-package-dir> <mobile-version> <field-sdk-version> <nuget-config>" >&2
    exit 2
fi
package_dir=$(realpath -e "$1")
mobile_version=$2
field_version=$3
config=$(realpath -e "$4")
repo=$(cd "$(dirname "$0")/.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf -- "$scratch"' EXIT
cd "$scratch"
cat > global.json <<'JSON'
{"sdk":{"version":"11.0.100","rollForward":"latestFeature"}}
JSON
dotnet pack "$repo/src/Honua.Mobile.Offline.Tasks/Honua.Mobile.Offline.Tasks.csproj" \
    --configuration Release --output "$package_dir" \
    -p:RestoreConfigFile="$config" -p:HonuaWorkTasksSdkVersion="$field_version" \
    -p:PackageVersion="$mobile_version" -p:Version="$mobile_version" -p:TreatWarningsAsErrors=true

cp "$config" NuGet.config
dotnet nuget add source "$package_dir" --name local-mobile --configfile NuGet.config
test_project="$repo/tests/Honua.Mobile.Offline.Tasks.Tests/Honua.Mobile.Offline.Tasks.Tests.csproj"
export NUGET_PACKAGES="$scratch/consumer-packages"
dotnet restore "$test_project" --configfile "$scratch/NuGet.config" \
    -p:HonuaMobileTasksPackageVersion="$mobile_version" -p:TreatWarningsAsErrors=true
python3 - "$test_project" "$package_dir" "$mobile_version" "$field_version" <<'PY'
import filecmp
import json
import os
import sys
from pathlib import Path

project, output, mobile_version, field_version = sys.argv[1:]
assets = json.loads(Path(project).with_name("obj").joinpath("project.assets.json").read_text())
libraries = assets["libraries"]
if f"Honua.Sdk.Field/{field_version}" not in libraries:
    sys.exit(f"Adapter did not resolve Honua.Sdk.Field {field_version}")
for name in ("Honua.Mobile.Sdk", "Honua.Mobile.Offline", "Honua.Mobile.Offline.Tasks"):
    entry = libraries.get(f"{name}/{mobile_version}", {})
    if entry.get("type") != "package":
        sys.exit(f"Adapter consumer did not resolve {name} {mobile_version} as a package")
    filename = f"{name}.{mobile_version}.nupkg"
    restored = Path(os.environ["NUGET_PACKAGES"], entry["path"], filename.lower())
    if not filecmp.cmp(Path(output, filename), restored, shallow=False):
        sys.exit(f"Restored bytes differ from {filename}")
PY

cat > adapter.runsettings <<'XML'
<RunSettings><RunConfiguration><TreatNoTestsAsError>true</TreatNoTestsAsError></RunConfiguration></RunSettings>
XML
dotnet test "$test_project" --configuration Release --no-restore \
    -p:HonuaMobileTasksPackageVersion="$mobile_version" -p:TreatWarningsAsErrors=true \
    --filter 'FullyQualifiedName~OfflineAssignedWorkTests' --settings "$scratch/adapter.runsettings"
