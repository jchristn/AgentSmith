"""Seed Azurite with the Hugo storage testbed fixtures.

Uses only the Python standard library and Shared Key authentication with the well-known Azurite development
account. Creates the container (idempotent) and uploads every file under the fixture directory, preserving relative
paths as blob names.
"""

import base64
import datetime
import hashlib
import hmac
import mimetypes
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

ACCOUNT = os.environ.get("AZURITE_ACCOUNT", "devstoreaccount1")
KEY = os.environ.get(
    "AZURITE_KEY",
    "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==",
)
ENDPOINT = os.environ.get("AZURITE_ENDPOINT", "http://azurite:10000/devstoreaccount1")
CONTAINER = os.environ.get("AZURITE_CONTAINER", "hugo-testbed")
FIXTURES = os.environ.get("FIXTURES", "/fixtures/hugo-testbed")
API_VERSION = "2021-08-06"


def sign(method, path, query, headers):
    canonical_headers = "".join(
        k + ":" + headers[k] + "\n" for k in sorted(h for h in headers if h.startswith("x-ms-"))
    )
    resource = "/" + ACCOUNT + path
    for name in sorted(query):
        resource += "\n" + name.lower() + ":" + query[name]
    length = headers.get("content-length", "")
    if length == "0":
        length = ""
    parts = [
        method,
        "",
        "",
        length,
        "",
        headers.get("content-type", ""),
        "",
        "",
        "",
        "",
        "",
        "",
        canonical_headers + resource,
    ]
    digest = hmac.new(base64.b64decode(KEY), "\n".join(parts).encode("utf-8"), hashlib.sha256).digest()
    return "SharedKey " + ACCOUNT + ":" + base64.b64encode(digest).decode("ascii")


def request(method, sub_path, query=None, body=b"", content_type=None, extra=None):
    query = query or {}
    base_path = urllib.request.urlparse(ENDPOINT).path.rstrip("/")
    path = base_path + "/" + sub_path
    headers = {
        "x-ms-date": datetime.datetime.now(datetime.timezone.utc).strftime("%a, %d %b %Y %H:%M:%S GMT"),
        "x-ms-version": API_VERSION,
        "content-length": str(len(body)),
    }
    if content_type:
        headers["content-type"] = content_type
    if extra:
        headers.update(extra)
    headers["authorization"] = sign(method, path, query, headers)
    url = ENDPOINT.rstrip("/") + "/" + urllib.parse.quote(sub_path)
    if query:
        url += "?" + urllib.parse.urlencode(query)
    req = urllib.request.Request(url, data=body if len(body) > 0 else None, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            return resp.status
    except urllib.error.HTTPError as e:
        return e.code


def main():
    for attempt in range(30):
        try:
            status = request("PUT", CONTAINER, {"restype": "container"})
            break
        except urllib.error.URLError:
            time.sleep(1)
    else:
        print("azurite not reachable", file=sys.stderr)
        return 1
    if status not in (201, 409):
        print("container create failed with status " + str(status), file=sys.stderr)
        return 1

    count = 0
    for root, _, files in os.walk(FIXTURES):
        for name in sorted(files):
            full = os.path.join(root, name)
            blob = os.path.relpath(full, FIXTURES).replace(os.sep, "/")
            with open(full, "rb") as f:
                data = f.read()
            content_type = mimetypes.guess_type(name)[0] or "application/octet-stream"
            status = request(
                "PUT",
                CONTAINER + "/" + blob,
                body=data,
                content_type=content_type,
                extra={"x-ms-blob-type": "BlockBlob"},
            )
            if status != 201:
                print("upload of " + blob + " failed with status " + str(status), file=sys.stderr)
                return 1
            count += 1
    print("seeded " + str(count) + " blobs into " + CONTAINER)
    return 0


if __name__ == "__main__":
    sys.exit(main())
