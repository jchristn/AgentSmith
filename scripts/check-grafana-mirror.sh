#!/usr/bin/env bash
# Validates the Grafana dashboard JSON in assets/grafana/ (the source of truth, mounted into Grafana by
# docker/compose.yaml) and, when they exist, the mirrors in docker/grafana/ and docker/factory/grafana/:
#
#   - every file parses as JSON and has a non-empty uid and title;
#   - uids are unique across the source directory and match the file name (<uid>.json);
#   - every datasource reference uses a known UID (prometheus, tempo, loki, or Grafana's built-in ones);
#   - every panel (rows excepted) has a description;
#   - each mirror holds exactly the same files with identical content.
#
# Usage: scripts/check-grafana-mirror.sh [repository root]
# Exit code 0 when every check passes, 1 otherwise. Needs python3.

set -euo pipefail

ROOT="${1:-$(cd "$(dirname "$0")/.." && pwd)}"
SOURCE="$ROOT/assets/grafana"
MIRRORS=("$ROOT/docker/grafana" "$ROOT/docker/factory/grafana")

if [ ! -d "$SOURCE" ]; then
  echo "check-grafana-mirror: $SOURCE not found" >&2
  exit 1
fi

python3 - "$SOURCE" "${MIRRORS[@]}" <<'PY'
import json
import os
import sys

KNOWN = {"prometheus", "tempo", "loki", "-- Grafana --", "-- Mixed --", "-- Dashboard --", "grafana"}
source = sys.argv[1]
mirrors = sys.argv[2:]
errors = []


def walk_datasources(node, path, out):
    if isinstance(node, dict):
        ds = node.get("datasource")
        if isinstance(ds, dict) and "uid" in ds:
            out.append((path, ds.get("uid")))
        elif isinstance(ds, str):
            out.append((path, ds))
        for key, value in node.items():
            walk_datasources(value, path + "/" + str(key), out)
    elif isinstance(node, list):
        for i, value in enumerate(node):
            walk_datasources(value, path + "/" + str(i), out)


def panels(dash):
    for p in dash.get("panels", []):
        yield p
        for inner in p.get("panels", []) or []:
            yield inner


files = sorted(f for f in os.listdir(source) if f.endswith(".json"))
if not files:
    errors.append("%s: no dashboard JSON files" % source)

uids = {}
for name in files:
    path = os.path.join(source, name)
    try:
        with open(path, encoding="utf-8") as fh:
            dash = json.load(fh)
    except Exception as e:
        errors.append("%s: invalid JSON: %s" % (name, e))
        continue
    uid = dash.get("uid")
    if not uid or not dash.get("title"):
        errors.append("%s: missing uid or title" % name)
        continue
    if uid in uids:
        errors.append("%s: uid %s already used by %s" % (name, uid, uids[uid]))
    uids[uid] = name
    if name != uid + ".json":
        errors.append("%s: file name does not match uid %s" % (name, uid))
    refs = []
    walk_datasources(dash, "", refs)
    for where, ds_uid in refs:
        if isinstance(ds_uid, str) and ds_uid.startswith("$"):
            continue
        if ds_uid not in KNOWN:
            errors.append("%s: unknown datasource uid %r at %s" % (name, ds_uid, where))
    ids = set()
    for p in panels(dash):
        if p.get("id") in ids:
            errors.append("%s: duplicate panel id %s" % (name, p.get("id")))
        ids.add(p.get("id"))
        if p.get("type") != "row" and not str(p.get("description", "")).strip():
            errors.append("%s: panel %r has no description" % (name, p.get("title")))
    print("ok   %-28s uid=%-20s panels=%d" % (name, uid, sum(1 for p in panels(dash) if p.get("type") != "row")))

for mirror in mirrors:
    if not os.path.isdir(mirror):
        print("skip %s (no mirror directory)" % mirror)
        continue
    mirrored = sorted(f for f in os.listdir(mirror) if f.endswith(".json"))
    for name in sorted(set(files) | set(mirrored)):
        a = os.path.join(source, name)
        b = os.path.join(mirror, name)
        if not os.path.exists(b):
            errors.append("%s: missing from mirror %s" % (name, mirror))
        elif not os.path.exists(a):
            errors.append("%s: in mirror %s but not in %s" % (name, mirror, source))
        elif open(a, "rb").read() != open(b, "rb").read():
            errors.append("%s: mirror %s differs from %s" % (name, mirror, source))
    print("ok   mirror %s" % mirror if not any(mirror in e for e in errors) else "FAIL mirror %s" % mirror)

if errors:
    for e in errors:
        print("FAIL " + e, file=sys.stderr)
    sys.exit(1)
print("check-grafana-mirror: %d dashboards valid" % len(files))
PY
