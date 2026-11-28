#!/usr/bin/env python3
# Copyright (c) Honua. All rights reserved.
# Licensed under the Apache License, Version 2.0.

"""Validate the four Mobile NuGet projects and write a release candidate receipt."""

import argparse
import json
import re
import xml.etree.ElementTree as ET
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from pathlib import Path

PACKAGE_IDS = (
    "Honua.Mobile.Sdk",
    "Honua.Mobile.Offline",
    "Honua.Mobile.Maui",
    "Honua.Mobile.Offline.Tasks",
)
VERSION = re.compile(r"(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*)){2}(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?")


def release_candidate(root: Path, version: str, commit: str) -> dict:
    if not VERSION.fullmatch(version):
        raise ValueError("Expected a canonical Mobile NuGet major.minor.patch version")
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise ValueError("Expected the full candidate commit SHA")
    tasks_sdk = ET.parse(root / "Directory.Build.props").findtext(".//HonuaWorkTasksSdkVersion", "").strip()
    if not VERSION.fullmatch(tasks_sdk):
        raise ValueError("Checked-in HonuaWorkTasksSdkVersion must be an exact version")
    for package_id in PACKAGE_IDS:
        project = root / "src" / package_id / f"{package_id}.csproj"
        package_version = ET.parse(project).findtext(".//PackageVersion", "").strip()
        if package_version != version:
            raise ValueError(f"{package_id} version {package_version!r} does not match {version!r}")
    return {"commit": commit, "version": version, "packages": list(PACKAGE_IDS), "tasksSdk": tasks_sdk}


def require_unpublished(version: str) -> None:
    lower_version = version.lower()
    for package in PACKAGE_IDS:
        lower_id = package.lower()
        base = f"https://api.nuget.org/v3-flatcontainer/{lower_id}/{lower_version}"
        url = f"{base}/{lower_id}.{lower_version}.nupkg"
        try:
            with urlopen(Request(url, method="HEAD"), timeout=30):
                raise ValueError(f"{package} {version} already exists on nuget.org")
        except HTTPError as error:
            if error.code != 404:
                raise ValueError(f"Could not prove {package} {version} is absent on nuget.org (HTTP {error.code})") from error


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("version")
    parser.add_argument("commit")
    parser.add_argument("--packages", type=Path, help="Directory containing the built candidate packages")
    parser.add_argument("--tasks-sdk", help="Honua.Sdk.Field version used to build the candidate")
    parser.add_argument("--require-unpublished", action="store_true")
    args = parser.parse_args()

    try:
        candidate = release_candidate(Path("."), args.version, args.commit)
        if args.packages is not None:
            if args.tasks_sdk != candidate["tasksSdk"]:
                raise ValueError("Candidate used a different Honua.Sdk.Field version than the checked-in pin")
            expected = {f"{package}.{args.version}.nupkg" for package in PACKAGE_IDS}
            actual = {path.name for path in args.packages.glob("*.nupkg") if path.is_file()}
            if actual != expected:
                raise ValueError(f"Expected the four exact candidate packages, found {sorted(actual)}")
        if args.require_unpublished:
            require_unpublished(args.version)
    except (OSError, ET.ParseError, ValueError) as error:
        parser.exit(1, f"{error}\n")

    print(json.dumps(candidate, sort_keys=True))


if __name__ == "__main__":
    main()
