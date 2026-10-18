#!/usr/bin/env python3
"""Import the synthetic log schema and attach it to an explicitly selected agent.

Environment: HUGO_URL, HUGO_TOKEN, HUGO_PROFILE_ID. Optional HUGO_DOCUMENT_MODE
is Reference (default) or Inline. The script never updates an existing document
or attachment, and never publishes a chat link. Run on an isolated demo tenant.
"""

import json
import os
from pathlib import Path
import sys
import urllib.request


def call(method, path, body=None):
    """Call the public API without exposing tokens or response bodies in errors."""
    payload = None if body is None else json.dumps(body).encode("utf-8")
    request = urllib.request.Request(
        os.environ.get("HUGO_URL", "http://127.0.0.1:8800").rstrip("/") + path,
        data=payload,
        method=method,
        headers={"Authorization": "Bearer " + os.environ["HUGO_TOKEN"],
                 "Content-Type": "application/json"},
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        text = response.read().decode("utf-8")
        return json.loads(text) if text else None


def main():
    """Create one managed copy, attach it with optimistic concurrency, and preview."""
    profile_id = os.environ["HUGO_PROFILE_ID"]
    mode = os.environ.get("HUGO_DOCUMENT_MODE", "Reference")
    if mode not in ("Inline", "Reference"):
        raise ValueError("HUGO_DOCUMENT_MODE must be Inline or Reference")
    base = "/v1.0/api/agent-profiles/" + profile_id + "/documents"
    current = call("GET", base)
    document = call("POST", "/v1.0/api/prompt-documents", {
        "name": "LOG_MESSAGE_STRUCTURE.md",
        "description": "Read before analyzing application logs; defines event fields and severity meanings.",
        "markdown": Path(__file__).with_name("LOG_MESSAGE_STRUCTURE.md").read_text(encoding="utf-8"),
        "visibility": "Tenant",
        "active": True,
    })
    try:
        attachment = call("POST", base, {
            "expectedDocumentSetVersion": current["documentSetVersion"],
            "documentId": document["id"], "deliveryMode": mode,
            "revisionPolicy": "Latest", "displayOrder": len(current["items"]),
            "enabled": True, "allowPublicAccess": False,
        })
    except Exception:
        call("DELETE", "/v1.0/api/prompt-documents/" + document["id"] +
             "?expectedVersion=" + str(document["version"]))
        raise
    preview = call("POST", base + "/preview", {"audience": "Authenticated"})
    print(json.dumps({"documentId": document["id"], "attachmentId": attachment["id"],
                      "mode": mode, "issues": preview["issues"],
                      "cleanup": "Detach the attachment with its current row/set versions, then delete the document."}, indent=2))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (KeyError, ValueError, OSError):
        print("Example failed. Check the required environment, permissions, document name and current versions.", file=sys.stderr)
        sys.exit(1)
