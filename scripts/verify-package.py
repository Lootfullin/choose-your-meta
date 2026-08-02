#!/usr/bin/env python3

import json
import sys
import zipfile
from pathlib import Path


def fail(message: str) -> None:
    raise SystemExit(f"Package verification failed: {message}")


if len(sys.argv) != 5:
    fail("expected archive, plugin version, target ABI and DLL name")

archive = Path(sys.argv[1])
expected_version = sys.argv[2]
expected_abi = sys.argv[3]
dll_name = sys.argv[4]

with zipfile.ZipFile(archive) as package:
    entries = {name for name in package.namelist() if not name.endswith("/")}
    expected_entries = {"meta.json", dll_name}
    if entries != expected_entries:
        fail(
            "archive root must contain exactly "
            f"{sorted(expected_entries)}, found {sorted(entries)}"
        )

    metadata = json.loads(package.read("meta.json"))

if metadata.get("autoUpdate") is not True:
    fail("meta.json must set autoUpdate to true")
if metadata.get("version") != expected_version:
    fail(
        f"meta.json version is {metadata.get('version')!r}, "
        f"expected {expected_version!r}"
    )
if metadata.get("targetAbi") != expected_abi:
    fail(
        f"meta.json targetAbi is {metadata.get('targetAbi')!r}, "
        f"expected {expected_abi!r}"
    )

print(f"Verified Jellyfin package: {archive}")
