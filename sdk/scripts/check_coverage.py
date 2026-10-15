#!/usr/bin/env python3
"""SDK coverage check: compare the server's /openapi.json operations against the three SDKs.

For every documented operation, each SDK's source must mention every literal path segment of the route (for example
`credentials` and `rotate` for POST /v1.0/api/tenants/{tenantId}/credentials/{id}/rotate). It is a gap detector, not a
proof of correctness: the live harnesses verify behavior.

Usage:
    python3 sdk/scripts/check_coverage.py [--server http://127.0.0.1:8800] [--openapi path/to/openapi.json]

Exit code 0 when every operation is covered by every SDK, 1 when there are gaps, 2 when the document cannot be read.
"""

import argparse
import json
import os
import re
import sys
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SDKS = {
    "csharp": (os.path.join(ROOT, "csharp", "src", "Hugo.Sdk"), (".cs",)),
    "python": (os.path.join(ROOT, "python", "hugo_sdk"), (".py",)),
    "js": (os.path.join(ROOT, "js", "src"), (".ts",)),
}

# Operations the SDKs intentionally do not wrap, with the reason.
EXCLUDED = {
    "/openapi.json": "documentation",
    "/swagger": "documentation",
    "/metrics": "Prometheus scrape endpoint",
    "/v1.0/api/health/ready": "alias of /v1.0/api/health",
    "/v1.0/api/server-info": "alias of /v1.0/api/info",
    "/v1.0/api/settings/restart": "alias of /v1.0/api/system/restart",
    "/v1.0/api/settings/database/test": "operator diagnostics",
    "/v1.0/tenants/{tenantId}/users/{id}/permissions": "AUTHENTICATION.md alias of the /v1.0/api path",
    "/v1.0/tenants/{tenantId}/credentials/{id}/permissions": "AUTHENTICATION.md alias of the /v1.0/api path",
}
EXCLUDED_PREFIXES = {
    "/mcp": "MCP surface (use an MCP client)",
    "/v1.0/a2a": "A2A surface (use an A2A client)",
    "/v1.0/openai": "OpenAI-compatible surface (use an OpenAI client)",
    "/v1/": "OpenAI-compatible surface (use an OpenAI client)",
    "/v1.0/api/secrets": "flat alias of the tenant-scoped secrets routes",
    "/v1.0/api/sessions": "flat alias of the tenant-scoped sessions routes",
}
IGNORED_SEGMENTS = {"", "v1.0", "api"}


def load(args):
    if args.openapi:
        with open(args.openapi, "r", encoding="utf-8") as f:
            return json.load(f)
    with urllib.request.urlopen(args.server.rstrip("/") + "/openapi.json", timeout=15) as r:
        return json.loads(r.read().decode("utf-8"))


def source_text(folder, extensions):
    parts = []
    for base, dirs, files in os.walk(folder):
        dirs[:] = [d for d in dirs if d not in ("bin", "obj", "node_modules", "dist", "__pycache__")]
        for name in files:
            if name.endswith(extensions):
                with open(os.path.join(base, name), "r", encoding="utf-8") as f:
                    parts.append(f.read())
    return "\n".join(parts)


def literals(path):
    return [s for s in path.split("/") if s not in IGNORED_SEGMENTS and not s.startswith("{")]


def covered(text, path):
    for segment in literals(path):
        if not re.search(r"[\"'/`]" + re.escape(segment) + r"[\"'/`?]", text):
            return False
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--server", default=os.environ.get("HUGO_SDK_SERVER", "http://127.0.0.1:8800"))
    parser.add_argument("--openapi", default=None)
    args = parser.parse_args()
    try:
        document = load(args)
    except Exception as e:  # noqa: BLE001
        print("Cannot read the OpenAPI document: " + str(e))
        return 2

    sources = {name: source_text(folder, ext) for name, (folder, ext) in SDKS.items()}
    operations = 0
    skipped = 0
    gaps = []
    for path, ops in sorted(document.get("paths", {}).items()):
        for method in sorted(ops.keys()):
            if method.lower() not in ("get", "post", "put", "delete", "patch"):
                continue
            operations += 1
            reason = EXCLUDED.get(path) or next((r for p, r in EXCLUDED_PREFIXES.items() if path.startswith(p)), None)
            if reason:
                skipped += 1
                continue
            for name, text in sources.items():
                if not covered(text, path):
                    gaps.append((name, method.upper(), path))

    print("Operations: %d (%d excluded by design)" % (operations, skipped))
    for name in SDKS:
        missing = [g for g in gaps if g[0] == name]
        print("%-7s %s" % (name, "complete" if not missing else "%d gap(s)" % len(missing)))
        for _, method, path in missing:
            print("          %s %s" % (method, path))
    return 1 if gaps else 0


if __name__ == "__main__":
    sys.exit(main())
