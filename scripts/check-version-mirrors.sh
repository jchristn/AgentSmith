#!/usr/bin/env bash
# Verify product version mirrors, or explicitly synchronize them. Requires Python 3.11 or newer.
# Usage: scripts/check-version-mirrors.sh [--sync] [repository root]
set -euo pipefail

MODE=check
if [ "${1:-}" = "--sync" ]; then
  MODE=sync
  shift
fi
if [ "$#" -gt 1 ] || [[ "${1:-}" == --* ]]; then
  echo "Usage: scripts/check-version-mirrors.sh [--sync] [repository root]" >&2
  exit 2
fi
ROOT="${1:-$(cd "$(dirname "$0")/.." && pwd)}"
python3 - "$ROOT" "$MODE" <<'PY'
import ast
import json
from pathlib import Path
import re
import sys
import tomllib
import xml.etree.ElementTree as ET

root = Path(sys.argv[1])
source = root / "src/Directory.Build.props"
versions = ET.parse(source).findall(".//Version")
if len(versions) != 1 or not versions[0].text:
    sys.exit("FAIL src/Directory.Build.props must define exactly one non-empty Version")
expected = versions[0].text
if sys.argv[2] == "sync":
    # Prepare every edit before writing. The authoritative props file is never a write target.
    edits = {}
    for directory in ("dashboard", "sdk/js"):
        for name in ("package.json", "package-lock.json"):
            path = root / directory / name
            original = path.read_text(encoding="utf-8")
            data = json.loads(original)
            if "version" not in data:
                sys.exit(f"FAIL {path}: missing version field")
            data["version"] = expected
            if name == "package-lock.json":
                data["packages"][""]["version"] = expected
            if json.loads(original) != data:
                # Replace only the existing version fields, preserving inline arrays and lockfile content.
                updated, count = re.subn(r'(?m)^(  "version"\s*:\s*)"[^"\n]*"',
                                         lambda match: match[1] + json.dumps(expected), original)
                if count != 1:
                    sys.exit(f"FAIL {path}: expected one top-level version field")
                if name == "package-lock.json":
                    updated, count = re.subn(
                        r'(?ms)(^  "packages"\s*:\s*\{\s*""\s*:\s*\{[^}]*?"version"\s*:\s*)"[^"\n]*"',
                        lambda match: match[1] + json.dumps(expected), updated)
                    if count != 1:
                        sys.exit(f"FAIL {path}: expected one root package version field")
                if json.loads(updated) != data:
                    sys.exit(f"FAIL {path}: unexpected manifest layout")
                edits[path] = updated

    replacements = {
        "sdk/python/pyproject.toml": (
            r'(?ms)(^\[project\]\s*\n(?:(?!^\[).)*?^version\s*=\s*)"[^"\n]*"',
            lambda match: match[1] + json.dumps(expected)),
        "sdk/python/hugo_sdk/version.py": (
            r'(?m)^(__version__\s*=\s*)[\'"][^\'"\n]*[\'"]',
            lambda match: match[1] + json.dumps(expected)),
        "sdk/csharp/src/Hugo.Sdk/Hugo.Sdk.csproj": (
            r'(<Version>)[^<]*(</Version>)',
            lambda match: match[1] + expected + match[2]),
    }
    for relative, (pattern, replacement) in replacements.items():
        path = root / relative
        original = path.read_text(encoding="utf-8")
        updated, count = re.subn(pattern, replacement, original)
        if count != 1:
            sys.exit(f"FAIL {relative}: expected exactly one version field, found {count}")
        if updated != original:
            edits[path] = updated
    for path, updated in edits.items():
        path.write_text(updated, encoding="utf-8")
        print(f"sync {path.relative_to(root)}")

mirrors = {}
for directory in ("dashboard", "sdk/js"):
    for name in ("package.json", "package-lock.json"):
        path = f"{directory}/{name}"
        data = json.loads((root / path).read_text(encoding="utf-8"))
        mirrors[path] = data.get("version")
        if name == "package-lock.json":
            mirrors[path + " packages['']"] = data.get("packages", {}).get("", {}).get("version")
path = "sdk/python/pyproject.toml"
with (root / path).open("rb") as stream:
    mirrors[path] = tomllib.load(stream).get("project", {}).get("version")
path = "sdk/python/hugo_sdk/version.py"
module = ast.parse((root / path).read_text(encoding="utf-8"))
runtime_versions = [ast.literal_eval(node.value) for node in module.body
                    if isinstance(node, ast.Assign)
                    and any(isinstance(target, ast.Name) and target.id == "__version__"
                            for target in node.targets)]
mirrors[path] = runtime_versions[0] if len(runtime_versions) == 1 else None
path = "sdk/csharp/src/Hugo.Sdk/Hugo.Sdk.csproj"
mirrors[path] = ET.parse(root / path).findtext(".//Version")
failed = False
for path, actual in mirrors.items():
    if actual != expected:
        print(f"FAIL {path}: {actual!r}; expected {expected!r}", file=sys.stderr)
        failed = True
    else:
        print(f"ok   {path}: {actual}")
if failed:
    sys.exit(1)
print(f"check-version-mirrors: {len(mirrors)} mirrors match {source.relative_to(root)}")
PY
