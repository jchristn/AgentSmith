"""Seed the Hugo observability testbed.

    python -I seed.py loki         push 40 log lines in two streams to Loki (tenant hugo-testbed)
    python -I seed.py opensearch   create and fill hugo-logs-2026.10 and hugo-secret, plus alias hugo-alias

Uses only the standard library. Endpoints come from LOKI_URL and OPENSEARCH_URL.
"""

import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

LOKI_URL = os.environ.get("LOKI_URL", "http://loki:3100")
OPENSEARCH_URL = os.environ.get("OPENSEARCH_URL", "http://opensearch:9200")
TENANT = os.environ.get("LOKI_TENANT", "hugo-testbed")


def request(method, url, body=None, headers=None, ok=(200, 201, 204)):
    data = None
    hdrs = dict(headers or {})
    if body is not None:
        data = body if isinstance(body, bytes) else json.dumps(body).encode("utf-8")
        hdrs.setdefault("Content-Type", "application/json")
    req = urllib.request.Request(url, data=data, method=method, headers=hdrs)
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            payload = resp.read().decode("utf-8")
            if resp.status not in ok:
                raise RuntimeError("%s %s returned %d: %s" % (method, url, resp.status, payload))
            return payload
    except urllib.error.HTTPError as e:
        if e.code in ok:
            return e.read().decode("utf-8")
        raise


def wait_for(url, headers=None, attempts=180):
    for _ in range(attempts):
        try:
            request("GET", url, headers=headers)
            return
        except Exception:  # noqa: BLE001 - keep waiting on any failure
            time.sleep(1)
    raise RuntimeError("timed out waiting for " + url)


def seed_loki():
    wait_for(LOKI_URL + "/ready")
    now_ns = time.time_ns()
    api = []
    worker = []
    for i in range(30):
        ts = str(now_ns - (40 - i) * 1_000_000_000)
        level = "error" if i % 5 == 0 else "info"
        api.append([ts, "level=%s msg=\"request %d handled\" status=%d path=/orders/%d" % (level, i, 500 if level == "error" else 200, i)])
    for i in range(10):
        ts = str(now_ns - (40 - i) * 1_000_000_000 - 500_000_000)
        worker.append([ts, "level=info msg=\"job %d finished\" queue=email" % i])
    body = {
        "streams": [
            {"stream": {"app": "hugo-api", "env": "testbed"}, "values": api},
            {"stream": {"app": "hugo-worker", "env": "testbed"}, "values": worker},
        ]
    }
    headers = {"X-Scope-OrgID": TENANT}
    # The ingester can refuse pushes for a few seconds after /ready; retry.
    for attempt in range(60):
        try:
            request("POST", LOKI_URL + "/loki/api/v1/push", body, headers)
            break
        except Exception:  # noqa: BLE001
            if attempt == 59:
                raise
            time.sleep(1)
    # Wait until the lines are queryable.
    query = urllib.parse.quote('{app="hugo-api"}')
    for _ in range(60):
        payload = json.loads(request("GET", LOKI_URL + "/loki/api/v1/query_range?limit=100&query=" + query, headers=headers))
        streams = payload.get("data", {}).get("result", [])
        if sum(len(s.get("values", [])) for s in streams) >= 30:
            return
        time.sleep(1)
    raise RuntimeError("seeded Loki lines did not become queryable")


def seed_opensearch():
    wait_for(OPENSEARCH_URL + "/_cluster/health?wait_for_status=yellow&timeout=1s")
    for index in ("hugo-logs-2026.10", "hugo-secret"):
        try:
            request("DELETE", OPENSEARCH_URL + "/" + index, ok=(200, 404))
        except urllib.error.HTTPError:
            pass
    mapping = {
        "settings": {"number_of_shards": 1, "number_of_replicas": 0},
        "mappings": {
            "properties": {
                "@timestamp": {"type": "date"},
                "level": {"type": "keyword"},
                "service": {"type": "keyword"},
                "message": {"type": "text", "fields": {"keyword": {"type": "keyword", "ignore_above": 256}}},
                "status": {"type": "integer"},
                "http": {"properties": {"method": {"type": "keyword"}, "path": {"type": "keyword"}}},
            }
        },
    }
    request("PUT", OPENSEARCH_URL + "/hugo-logs-2026.10", mapping)
    request("PUT", OPENSEARCH_URL + "/hugo-secret", {"settings": {"number_of_shards": 1, "number_of_replicas": 0}})
    lines = []
    for i in range(50):
        level = "error" if i % 10 == 0 else "info"
        doc = {
            "@timestamp": "2026-10-14T08:%02d:00Z" % (i % 60),
            "level": level,
            "service": "checkout" if i % 2 == 0 else "catalog",
            "message": "request %d %s" % (i, "failed with timeout" if level == "error" else "ok"),
            "status": 500 if level == "error" else 200,
            "http": {"method": "GET", "path": "/items/%d" % i},
        }
        lines.append(json.dumps({"index": {"_index": "hugo-logs-2026.10", "_id": "log-%02d" % i}}))
        lines.append(json.dumps(doc))
    for i in range(3):
        lines.append(json.dumps({"index": {"_index": "hugo-secret", "_id": "secret-%d" % i}}))
        lines.append(json.dumps({"message": "classified %d" % i}))
    body = ("\n".join(lines) + "\n").encode("utf-8")
    request("POST", OPENSEARCH_URL + "/_bulk?refresh=true", body, {"Content-Type": "application/x-ndjson"})
    request("POST", OPENSEARCH_URL + "/_aliases", {"actions": [{"add": {"index": "hugo-secret", "alias": "hugo-alias"}}]})
    count = json.loads(request("GET", OPENSEARCH_URL + "/hugo-logs-2026.10/_count"))["count"]
    if count != 50:
        raise RuntimeError("expected 50 documents, found %d" % count)


if __name__ == "__main__":
    target = sys.argv[1] if len(sys.argv) > 1 else ""
    if target == "loki":
        seed_loki()
    elif target == "opensearch":
        seed_opensearch()
    else:
        sys.exit("usage: seed.py loki|opensearch")
    print("seeded " + target, flush=True)
