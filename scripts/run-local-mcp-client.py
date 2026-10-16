#!/usr/bin/env python3
"""Run isolated Codex OSS or mux acceptance against an existing private P12.7 fixture.

Usage: HUGO_ACCEPTANCE_MODEL_URL=<Ollama base URL> python3 scripts/run-local-mcp-client.py STATE.json
Requires an already-built Hugo CLI, the selected installed client, and a loaded model.
The endpoint stays in the child environment. No user client settings are changed.
"""

import argparse
import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import tempfile
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import urllib.error
import urllib.request


def verify_protocol(work, root):
    """Verify actual client discovery and successful read-only calls, not merely its exit code."""
    events = [json.loads(line) for line in (work / "mcp-protocol.jsonl").read_text().splitlines()]
    requests = {}
    responses = {}
    for event in events:
        message = event["message"]
        if not isinstance(message, dict):
            continue
        if event["direction"] == "client-to-hugo" and "id" in message:
            requests[message["id"]] = message
        elif event["direction"] == "hugo-to-client" and "id" in message:
            responses[message["id"]] = message
    init = next(responses[key]["result"] for key, message in requests.items() if message.get("method") == "initialize")
    golden = (root / "src/Test.Shared/Fixtures/golden/joel-mcp-instructions.md").read_text()
    if init.get("instructions") != golden:
        raise ValueError("Actual client initialize instructions differ from the joel golden")
    tools = next(responses[key]["result"]["tools"] for key, message in requests.items() if message.get("method") == "tools/list")
    by_name = {tool["name"]: tool for tool in tools}
    for name, aliases in {"sql_query": ["legacy-db", "prod-db", "reports"], "ssh_exec": ["web-01"],
                          "logs_search": ["papertrail", "swo"], "storage_list": ["archive", "assets"]}.items():
        if by_name[name]["inputSchema"]["properties"]["resource"]["enum"] != aliases:
            raise ValueError("Unexpected resource enum for " + name)
    if any(name.startswith(("http_", "hugo_admin_")) for name in by_name):
        raise ValueError("Unbound HTTP or management tools were exposed")
    calls = {}
    for key, message in requests.items():
        if message.get("method") != "tools/call":
            continue
        name = message["params"]["name"]
        response = responses.get(key, {})
        if "error" in response or response.get("result", {}).get("isError"):
            raise ValueError("Actual MCP call failed: " + name)
        if "result" not in response:
            raise ValueError("Actual MCP call has no response: " + name)
        if name.startswith("sql_") and message["params"].get("arguments", {}).get("resource") != "reports":
            raise ValueError("Client queried a discovery-only fixture")
        calls[name] = response["result"]
    required = {"hugo_get_instructions", "hugo_list_resources", "sql_list_tables", "sql_query"}
    if not required.issubset(calls):
        raise ValueError("Actual client omitted required tools: " + ", ".join(sorted(required - calls.keys())))
    if set(calls) - required:
        raise ValueError("Actual client used an unexpected tool")
    instructions = "\n".join(item.get("text", "") for item in calls["hugo_get_instructions"].get("content", []))
    if instructions != golden:
        raise ValueError("hugo_get_instructions differs from the initialize golden")
    query = calls["sql_query"].get("structuredContent", {})
    if query.get("statementType") != "SELECT" or query.get("rows") != [["paid", "2"], ["pending", "1"]]:
        raise ValueError("Real SELECT returned unexpected order counts")
    return {"goldenInstructionsMatch": True, "instructionCharacters": len(golden), "filteredToolCount": len(tools),
            "successfulToolCalls": sorted(calls), "resourceEnumChecks": True, "orderCounts": {"paid": 2, "pending": 1}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("state", type=Path)
    parser.add_argument("--model", default="qwen3.8:27b")
    parser.add_argument("--client", choices=["codex", "mux"], default="codex")
    parser.add_argument("--timeout", type=int, default=420)
    arguments = parser.parse_args()
    endpoint = os.environ.get("HUGO_ACCEPTANCE_MODEL_URL")
    if not endpoint:
        parser.error("HUGO_ACCEPTANCE_MODEL_URL must name the prepared Ollama service")
    state = json.loads(arguments.state.read_text())
    work = Path(state["work"])
    root = Path(__file__).resolve().parents[1]
    spec = importlib.util.spec_from_file_location("acceptance_tap", root / "scripts/mcp-acceptance-tap.py")
    tap = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(tap)
    prompt = (
        "Perform this MCP acceptance check using only the hugo MCP tools. Do not use shell, file, web, "
        "or other tools. First call hugo_get_instructions and hugo_list_resources. Read the returned "
        "persona and list all eight aliases with their access modes. Then call sql_list_tables on resource "
        "reports, then sql_query on reports with SELECT status, COUNT(*) AS order_count FROM orders "
        "GROUP BY status ORDER BY status LIMIT 10. These are read-only calls. Do not call other resource "
        "tools or change any data. In the final answer address Mr. Anderson, identify the Agent Smith "
        "persona, list the eight resources, and report exact order counts with the source resource. "
        "Never reveal credentials or model endpoint addresses."
    )
    mcp = {"command": "python3", "args": [str(root / "scripts/mcp-acceptance-tap.py"), str(arguments.state.resolve())], "startup_timeout_sec": 45, "tool_timeout_sec": 60}
    env = os.environ.copy()
    env["CODEX_OSS_BASE_URL"] = endpoint.rstrip("/") + "/v1"
    proxy = None
    with tempfile.TemporaryDirectory(prefix="codex-p127-") as cwd:
        command = ["codex", "--no-daemon", "-a", "never", "exec", "--ignore-user-config", "--ignore-rules",
                   "--ephemeral", "--skip-git-repo-check", "--oss", "--local-provider", "ollama", "-m", arguments.model,
                   "--json", "--sandbox", "read-only", "-C", cwd]
        for key, value in mcp.items():
            command.extend(["-c", "mcp_servers.hugo." + key + "=" + json.dumps(value)])
        command.append(prompt)
        if arguments.client == "mux":
            # Only the in-memory proxy knows the private endpoint. Client configuration names loopback.
            class ProxyHandler(BaseHTTPRequestHandler):
                def log_message(self, *_args):
                    pass

                def forward(self):
                    body = self.rfile.read(int(self.headers.get("Content-Length", "0"))) if self.command == "POST" else None
                    if body:
                        data = json.loads(body)
                        if self.path == "/api/chat":
                            data["think"] = False
                            data.setdefault("options", {})["num_ctx"] = 32768
                            data["options"]["num_predict"] = 2048
                            body = json.dumps(data).encode()
                        messages = json.dumps(data.get("messages", []))
                        record = {"path": self.path, "model": data.get("model"), "think": data.get("think"),
                                  "options": data.get("options"), "messageCount": len(data.get("messages", [])),
                                  "toolCount": len(data.get("tools", [])), "personaPresent": "Agent Smith" in messages,
                                  "operatorAddressPresent": "Mr. Anderson" in messages,
                                  "eightResourcesPresent": "You have access to 8 resources" in messages}
                        with (work / "model-requests.jsonl").open("a") as output:
                            output.write(json.dumps(record) + "\n")
                    request = urllib.request.Request(endpoint.rstrip("/") + self.path, data=body, method=self.command,
                                                     headers={"Content-Type": self.headers.get("Content-Type", "application/json")})
                    try:
                        with urllib.request.urlopen(request, timeout=180) as response:
                            self.send_response(response.status)
                            self.send_header("Content-Type", response.headers.get("Content-Type", "application/json"))
                            self.end_headers()
                            while chunk := response.read(4096):
                                self.wfile.write(chunk)
                                self.wfile.flush()
                    except (urllib.error.URLError, OSError):
                        self.send_error(502, "Private model request failed")

                do_POST = forward
                do_GET = forward

            proxy = ThreadingHTTPServer(("127.0.0.1", 0), ProxyHandler)
            threading.Thread(target=proxy.serve_forever, daemon=True).start()
            config = Path(cwd) / "config"
            config.mkdir()
            mcp_config = config / "acceptance-mcp.json"
            mcp_config.write_text(json.dumps({"servers": [{"name": "hugo", "transport": "Stdio", "command": "python3", "args": mcp["args"]}]}))
            (config / "endpoints.json").write_text(json.dumps({"endpoints": [{"name": "acceptance", "adapterType": "Ollama",
                "baseUrl": "http://127.0.0.1:" + str(proxy.server_port), "model": arguments.model, "isDefault": True,
                "contextWindow": 32768, "maxTokens": 2048, "timeoutMs": 180000}]}))
            command = ["mux", "--print", "--config-dir", str(config), "--endpoint", "acceptance", "--output-format", "jsonl",
                       "--strict-mcp-config", "--mcp-config", str(mcp_config), "--no-project-instructions", "--no-session-persistence",
                       "--working-directory", cwd, "--approval-policy", "auto", "--allow-tools",
                       "hugo.hugo_get_instructions,hugo.hugo_list_resources,hugo.sql_list_tables,hugo.sql_query", "--sandbox", "none",
                       "--context-window", "32768", "--max-tokens", "2048", "--max-turns", "8", "--effort-ollama-think", "false", prompt]
        process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, start_new_session=True)
        timed_out = False
        try:
            stdout, stderr = process.communicate(timeout=arguments.timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
            os.killpg(process.pid, signal.SIGTERM)
            try:
                stdout, stderr = process.communicate(timeout=10)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                stdout, stderr = process.communicate()
        for name, data in (("client-events.jsonl", stdout), ("client-stderr.txt", stderr)):
            (work / name).write_text(tap.sanitize(data.decode("utf-8", errors="replace"), state))
        if proxy:
            proxy.shutdown()
            proxy.server_close()
        summary = {"client": "Codex OSS" if arguments.client == "codex" else "mux native Ollama", "model": arguments.model, "exitCode": process.returncode, "timedOut": timed_out,
                   "isolatedConfiguration": True, "privateEndpointRedacted": True}
        try:
            summary.update(verify_protocol(work, root))
            summary["protocolAccepted"] = True
        except (KeyError, StopIteration, ValueError, FileNotFoundError) as error:
            summary["protocolAccepted"] = False
            summary["validationError"] = str(error)
        (work / "client-run.json").write_text(json.dumps(summary, indent=2) + "\n")
        print(json.dumps(summary))
        return 1 if timed_out or not summary["protocolAccepted"] else process.returncode


if __name__ == "__main__":
    raise SystemExit(main())
