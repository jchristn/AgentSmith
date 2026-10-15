"""A tiny OpenAI-compatible model server for the harness, modeled on the server suites' StubLlmServer.

It answers on 127.0.0.1 with a free port:

- POST .../chat/completions: a fixed assistant reply, streamed as SSE chunks (``choices[0].delta.content``, then a
  ``finish_reason`` of ``stop``, a usage chunk, and ``data: [DONE]``) when the request has ``"stream": true``, otherwise
  as one ``chat.completion`` object with usage.
- GET .../models: one model.
- POST .../embeddings: a deterministic 8-dimension vector per input.

Every chat request body is recorded in ``chat_requests`` so tests can check what Hugo sent.
"""

from __future__ import annotations

import hashlib
import json
import threading
import time
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Dict, List

MODEL_NAME = "stub-model"
DIMENSIONS = 8


class StubLlm:
    """Start with ``StubLlm()``; stop with ``close()``. ``reply`` is the assistant text returned for every chat call."""

    def __init__(self, reply: str = "Good evening, Mr. Anderson. The stub is listening.") -> None:
        self.reply = reply
        self.chat_requests: List[Dict[str, Any]] = []
        self._lock = threading.Lock()
        stub = self

        class Handler(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, format: str, *args: Any) -> None:  # noqa: A002 - signature from the base class
                return

            def _json(self, status: int, payload: Any) -> None:
                data = json.dumps(payload).encode("utf-8")
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)

            def _body(self) -> Dict[str, Any]:
                length = int(self.headers.get("Content-Length") or 0)
                raw = self.rfile.read(length) if length > 0 else b""
                try:
                    return json.loads(raw.decode("utf-8")) if raw else {}
                except ValueError:
                    return {}

            def do_GET(self) -> None:  # noqa: N802 - http.server naming
                if self.path.split("?")[0].endswith("/models"):
                    self._json(200, {"object": "list", "data": [{"id": MODEL_NAME, "object": "model", "owned_by": "stub"}]})
                else:
                    self._json(404, {"error": {"message": "not found", "type": "not_found"}})

            def do_POST(self) -> None:  # noqa: N802 - http.server naming
                path = self.path.split("?")[0]
                body = self._body()
                if path.endswith("/chat/completions"):
                    with stub._lock:
                        stub.chat_requests.append(body)
                    self._chat(body)
                elif path.endswith("/embeddings"):
                    inputs = body.get("input") or []
                    if isinstance(inputs, str):
                        inputs = [inputs]
                    data = [{"object": "embedding", "index": i, "embedding": _embed(str(text))} for i, text in enumerate(inputs)]
                    tokens = max(1, sum(len(str(t)) for t in inputs) // 4)
                    self._json(200, {"object": "list", "data": data, "model": body.get("model") or MODEL_NAME,
                                     "usage": {"prompt_tokens": tokens, "total_tokens": tokens}})
                else:
                    self._json(404, {"error": {"message": "not found", "type": "not_found"}})

            def _chat(self, body: Dict[str, Any]) -> None:
                text = stub.reply
                messages = body.get("messages") or []
                prompt_tokens = max(1, sum(len(str(m.get("content") or "")) for m in messages if isinstance(m, dict)) // 4)
                completion_tokens = max(1, len(text) // 4)
                usage = {"prompt_tokens": prompt_tokens, "completion_tokens": completion_tokens,
                         "total_tokens": prompt_tokens + completion_tokens}
                cid = "chatcmpl-" + uuid.uuid4().hex[:16]
                created = int(time.time())
                if not body.get("stream"):
                    self._json(200, {"id": cid, "object": "chat.completion", "created": created, "model": MODEL_NAME,
                                     "choices": [{"index": 0, "message": {"role": "assistant", "content": text}, "finish_reason": "stop"}],
                                     "usage": usage})
                    return
                self.send_response(200)
                self.send_header("Content-Type", "text/event-stream")
                self.send_header("Cache-Control", "no-cache")
                self.send_header("Connection", "close")
                self.end_headers()

                def chunk(delta: Dict[str, Any], finish: Any) -> None:
                    payload = {"id": cid, "object": "chat.completion.chunk", "created": created, "model": MODEL_NAME,
                               "choices": [{"index": 0, "delta": delta, "finish_reason": finish}]}
                    self.wfile.write(("data: " + json.dumps(payload) + "\n\n").encode("utf-8"))
                    self.wfile.flush()

                first = True
                for i in range(0, len(text), 8):
                    delta: Dict[str, Any] = {"content": text[i:i + 8]}
                    if first:
                        delta["role"] = "assistant"
                        first = False
                    chunk(delta, None)
                chunk({}, "stop")
                usage_chunk = {"id": cid, "object": "chat.completion.chunk", "created": created, "model": MODEL_NAME,
                               "choices": [], "usage": usage}
                self.wfile.write(("data: " + json.dumps(usage_chunk) + "\n\n").encode("utf-8"))
                self.wfile.write(b"data: [DONE]\n\n")
                self.wfile.flush()
                self.close_connection = True

        self._server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self._server.daemon_threads = True
        self.port = self._server.server_address[1]
        self.base_url = f"http://127.0.0.1:{self.port}"
        self._thread = threading.Thread(target=self._server.serve_forever, daemon=True)
        self._thread.start()

    def close(self) -> None:
        self._server.shutdown()
        self._server.server_close()


def _embed(text: str) -> List[float]:
    digest = hashlib.sha256(text.encode("utf-8")).digest()
    return [round((b - 128) / 128.0, 6) for b in digest[:DIMENSIONS]]
