#!/usr/bin/env python3
"""Run the built Hugo stdio relay and retain a sanitized acceptance protocol trace.

The private state file supplies port, accessKey, secretKey, tenantId, and work.
This helper is for the isolated P12.7 fixture, not operator credential storage.
"""

import json
import os
from pathlib import Path
import re
import subprocess
import sys
import threading


def sanitize(text, state):
    """Remove fixture credentials and private network addresses from evidence."""
    text = text.replace("\u2014", " - ")
    for key in ("accessKey", "secretKey", "adminKey", "token"):
        value = state.get(key)
        if value:
            text = text.replace(value, "<redacted>")
    model_url = os.environ.get("HUGO_ACCEPTANCE_MODEL_URL", "")
    if model_url:
        text = text.replace(model_url, "<private-model-endpoint>")
    return re.sub(r"\b(?:192\.168\.\d{1,3}\.\d{1,3}|10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3})\b", "<private-address>", text)


def main():
    state = json.loads(Path(sys.argv[1]).read_text())
    root = Path(__file__).resolve().parents[1]
    env = {key: value for key, value in os.environ.items() if not key.startswith("HUGO_")}
    env.update(HUGO_ACCESS_KEY=state["accessKey"], HUGO_SECRET_KEY=state["secretKey"], HUGO_TENANT=state["tenantId"])
    process = subprocess.Popen([
        "dotnet", str(root / "src/Hugo.Cli/bin/Debug/net10.0/hugo.dll"),
        "mcp", "stdio", "--server", "http://127.0.0.1:" + str(state["port"]),
        "--profile", "Hugo", "--credentials-file", str(Path(state["work"]) / "unused-cli-cache.json")
    ], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env)
    lock = threading.Lock()
    trace = Path(state["work"]) / "mcp-protocol.jsonl"

    def record(direction, line):
        safe = sanitize(line.decode("utf-8", errors="replace").rstrip(), state)
        try:
            message = json.loads(safe)
        except json.JSONDecodeError:
            message = safe
        with lock, trace.open("a") as output:
            output.write(json.dumps({"direction": direction, "message": message}) + "\n")

    def incoming():
        try:
            for line in sys.stdin.buffer:
                record("client-to-hugo", line)
                process.stdin.write(line)
                process.stdin.flush()
        except (BrokenPipeError, OSError):
            pass
        finally:
            process.stdin.close()

    def diagnostics():
        for line in process.stderr:
            record("stderr", line)
            sys.stderr.write(sanitize(line.decode("utf-8", errors="replace"), state))
            sys.stderr.flush()

    threading.Thread(target=incoming, daemon=True).start()
    threading.Thread(target=diagnostics, daemon=True).start()
    try:
        for line in process.stdout:
            record("hugo-to-client", line)
            sys.stdout.buffer.write(line)
            sys.stdout.buffer.flush()
        return process.wait(timeout=10)
    finally:
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()


if __name__ == "__main__":
    raise SystemExit(main())
