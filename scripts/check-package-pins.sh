#!/usr/bin/env bash
# Check direct NuGet references against the reviewed version inventory. No network or manifest writes.
# Usage: scripts/check-package-pins.sh [repository root]
set -euo pipefail
ROOT="${1:-$(cd "$(dirname "$0")/.." && pwd)}"
python3 - "$ROOT" <<'PY'
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

root = Path(sys.argv[1])
pins = json.loads((root / "scripts/package-pins.json").read_text(encoding="utf-8"))
seen = set()
errors = []
count = 0
for project in sorted((root / "src").rglob("*.csproj")):
    for reference in ET.parse(project).findall(".//PackageReference"):
        name = reference.get("Include")
        version = reference.get("Version")
        count += 1
        seen.add(name)
        if version is None or not re.fullmatch(r"\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?", version):
            errors.append(f"{project.relative_to(root)}: {name} must have a fixed Version, got {version!r}")
        elif pins.get(name) != version:
            errors.append(f"{project.relative_to(root)}: {name} {version} differs from reviewed pin {pins.get(name)!r}")
for name in sorted(set(pins) - seen):
    errors.append(f"{name}: reviewed pin has no direct project reference")
if errors:
    for error in errors:
        print("FAIL " + error, file=sys.stderr)
    sys.exit(1)
print(f"check-package-pins: {count} direct references match {len(pins)} reviewed versions")
PY
