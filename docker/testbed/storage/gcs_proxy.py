"""Timestamp-normalizing reverse proxy for fake-gcs-server.

fake-gcs-server formats timestamps with Go's RFC3339Nano, which trims trailing zeros from the fractional seconds
(for example 2026-10-15T00:29:12.94368Z). The Google .NET client (Google.Apis.Storage.v1) only parses fractions of
0, 3, 6, or 9 digits and throws FormatException otherwise. Real Google Cloud Storage always returns millisecond
precision, so this only affects the emulator. The proxy forwards every request unchanged and pads fractional seconds
in JSON responses to nine digits.
"""

import http.client
import http.server
import os
import re
import socketserver
import sys

UPSTREAM_HOST = os.environ.get("UPSTREAM_HOST", "fake-gcs")
UPSTREAM_PORT = int(os.environ.get("UPSTREAM_PORT", "4443"))
LISTEN_PORT = int(os.environ.get("LISTEN_PORT", "4443"))
TIMESTAMP = re.compile(rb'"(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d)\.(\d{1,9})Z"')
HOP_HEADERS = {"connection", "keep-alive", "transfer-encoding", "te", "trailer", "upgrade", "content-length", "host"}


def pad(match):
    return b'"' + match.group(1) + b"." + match.group(2).ljust(9, b"0") + b'Z"'


class Handler(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        pass

    def read_body(self):
        if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
            chunks = []
            while True:
                size = int(self.rfile.readline().strip().split(b";")[0], 16)
                if size == 0:
                    self.rfile.readline()
                    break
                chunks.append(self.rfile.read(size))
                self.rfile.readline()
            return b"".join(chunks)
        length = int(self.headers.get("Content-Length", "0") or "0")
        return self.rfile.read(length) if length > 0 else b""

    def forward(self):
        body = self.read_body()
        headers = {k: v for k, v in self.headers.items() if k.lower() not in HOP_HEADERS}
        headers["Host"] = self.headers.get("Host", "127.0.0.1")
        if body or self.command in ("POST", "PUT", "PATCH"):
            headers["Content-Length"] = str(len(body))
        conn = http.client.HTTPConnection(UPSTREAM_HOST, UPSTREAM_PORT, timeout=60)
        try:
            conn.request(self.command, self.path, body=body if body else None, headers=headers)
            resp = conn.getresponse()
            data = resp.read()
            content_type = resp.getheader("Content-Type", "")
            if "json" in content_type:
                data = TIMESTAMP.sub(pad, data)
            self.send_response(resp.status, resp.reason)
            for k, v in resp.getheaders():
                if k.lower() not in HOP_HEADERS:
                    self.send_header(k, v)
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            if self.command != "HEAD":
                self.wfile.write(data)
        finally:
            conn.close()

    do_GET = forward
    do_POST = forward
    do_PUT = forward
    do_PATCH = forward
    do_DELETE = forward
    do_HEAD = forward


class Server(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


if __name__ == "__main__":
    print("gcs proxy listening on " + str(LISTEN_PORT), file=sys.stderr)
    Server(("0.0.0.0", LISTEN_PORT), Handler).serve_forever()
