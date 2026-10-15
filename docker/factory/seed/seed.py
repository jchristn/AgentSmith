"""Seeds the Hugo factory stack through the public REST API (plan P10.7 and P10.8).

Runs once per "docker compose up" as the hugo-seed service in docker/factory/compose.yaml, after hugo-server and the
demo targets report healthy. Every step is idempotent: objects are looked up by name and created or updated, so the
seed can run any number of times against the same data.

What it does:
  1. Signs in as anderson@hugo.local and replaces the first-boot password with the demo password.
  2. Creates the secrets for the demo targets (SSH key, database passwords, MinIO and LocalStack keys, Redis and
     Grafana logins).
  3. Creates ten resources pointing at the targets on the Compose network (wave 1: PostgreSQL, MySQL, SSH, S3;
     wave 2: Redis, Prometheus, Loki, HTTP, OpenSearch, AWS on LocalStack).
  4. Binds all ten to the default Hugo profile with mixed access modes (the joel scenario, plan section 10.5).
  5. Adds a model endpoint for a local Ollama when one answers at HUGO_OLLAMA_URL, and uses it for Hugo.
  6. Creates the demo credential access_hugo_demo_not_for_production, bound and locked to Hugo, and prints it.

Standard library only (the container is python:alpine). Configuration comes from the environment; see compose.yaml.
"""

import json
import os
import sys
import time
import urllib.error
import urllib.request

HUGO_URL = os.environ.get("HUGO_URL", "http://hugo-server:8800").rstrip("/")
PUBLIC_URL = os.environ.get("HUGO_PUBLIC_URL", "http://127.0.0.1:8810").rstrip("/")
DASHBOARD_URL = os.environ.get("HUGO_DASHBOARD_URL", "http://127.0.0.1:8811").rstrip("/")
GRAFANA_URL = os.environ.get("HUGO_GRAFANA_URL", "http://127.0.0.1:3010").rstrip("/")
TENANT = os.environ.get("HUGO_TENANT", "default")
EMAIL = os.environ.get("HUGO_EMAIL", "anderson@hugo.local")
FIRST_PASSWORD = os.environ.get("HUGO_FIRST_PASSWORD", "password")
DEMO_PASSWORD = os.environ.get("HUGO_DEMO_PASSWORD", "HugoDemo2026!")
OLLAMA_URL = os.environ.get("HUGO_OLLAMA_URL", "http://host.docker.internal:11434").rstrip("/")
OLLAMA_MODEL = os.environ.get("HUGO_OLLAMA_MODEL", "")
OLLAMA_MODEL_URL = os.environ.get("HUGO_OLLAMA_MODEL_URL", "").rstrip("/")
PROFILE_NAME = os.environ.get("HUGO_PROFILE", "Hugo")
CREDENTIAL_NAME = "access_hugo_demo_not_for_production"
SSH_KEY_FILE = os.environ.get("HUGO_SSH_KEY_FILE", "/ssh-keys/id_ed25519")
STATE_FILE = os.environ.get("HUGO_SEED_STATE", "/state/credential.json")
ENVIRONMENT = "demo"


class ApiError(Exception):
    """A REST call returned an unexpected status."""

    def __init__(self, method, path, status, body):
        super().__init__("%s %s returned %s: %s" % (method, path, status, body[:600]))
        self.status = status
        self.body = body


def log(message):
    print("[seed] " + message, flush=True)


def call(method, path, body=None, token=None, expect=(200, 201, 204)):
    data = None if body is None else json.dumps(body).encode("utf-8")
    request = urllib.request.Request(HUGO_URL + path, data=data, method=method)
    request.add_header("Accept", "application/json")
    if data is not None:
        request.add_header("Content-Type", "application/json")
    if token:
        request.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            status = response.status
            text = response.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        status = e.code
        text = e.read().decode("utf-8", "replace")
    if status not in expect:
        raise ApiError(method, path, status, text)
    return json.loads(text) if text.strip() else None


def objects(result):
    if isinstance(result, list):
        return result
    return (result or {}).get("objects", [])


def wait_for_server():
    deadline = time.time() + 300
    while True:
        try:
            call("GET", "/v1.0/api/health")
            return
        except Exception as e:  # noqa: BLE001 (any failure means "not yet")
            if time.time() > deadline:
                raise RuntimeError("Hugo did not become healthy at %s: %s" % (HUGO_URL, e))
            time.sleep(2)


def login(password):
    result = call("POST", "/v1.0/token", {"tenantId": TENANT, "email": EMAIL, "password": password}, expect=(200, 201, 401, 403))
    if not result or "token" not in result:
        return None
    return result["token"]


def sign_in():
    token = login(DEMO_PASSWORD)
    if token:
        log("signed in as %s with the demo password" % EMAIL)
        return token
    token = login(FIRST_PASSWORD)
    if not token:
        raise RuntimeError("Could not sign in as %s with the demo password or the first-boot password." % EMAIL)
    call("POST", "/v1.0/api/users/me/password", {"currentPassword": FIRST_PASSWORD, "newPassword": DEMO_PASSWORD}, token)
    log("changed the first-boot password of %s to the demo password" % EMAIL)
    token = login(DEMO_PASSWORD)
    if not token:
        raise RuntimeError("Sign-in with the new demo password failed.")
    return token


def find_by_name(token, path, name):
    result = call("GET", path + "?maxResults=1000", token=token)
    for item in objects(result):
        if item.get("name") == name:
            return item
    return None


def upsert_secret(token, name, kind, values, description):
    existing = find_by_name(token, "/v1.0/api/secrets", name)
    if existing:
        call("PUT", "/v1.0/api/secrets/%s/values" % existing["id"], {"values": values}, token)
        log("secret %-24s updated" % name)
        return existing["id"]
    created = call("POST", "/v1.0/api/secrets", {"name": name, "description": description, "kind": kind, "values": values}, token)
    log("secret %-24s created" % name)
    return created["id"]


def upsert_resource(token, spec, secret_ids):
    body = {
        "name": spec["name"],
        "connectorType": spec["type"],
        "displayName": spec.get("displayName"),
        "description": spec["description"],
        "environment": ENVIRONMENT,
        "notes": spec.get("notes"),
        "settings": spec["settings"],
        "secretBindings": [
            {"slotName": slot, "secretId": secret_ids[secret]} for slot, secret in spec.get("secrets", {}).items()
        ],
    }
    existing = find_by_name(token, "/v1.0/api/resources", spec["name"])
    if existing:
        call("PUT", "/v1.0/api/resources/%s" % existing["id"], body, token)
        log("resource %-10s (%s) updated" % (spec["name"], spec["type"]))
        return existing["id"]
    created = call("POST", "/v1.0/api/resources", body, token)
    log("resource %-10s (%s) created" % (spec["name"], spec["type"]))
    return created["id"]


def test_resource(token, resource_id, name, attempts=1):
    for attempt in range(attempts):
        if attempt:
            time.sleep(5)
        if try_resource(token, resource_id, name, attempt == attempts - 1):
            return True
    return False


def try_resource(token, resource_id, name, report):
    try:
        result = call("POST", "/v1.0/api/resources/%s/test" % resource_id, token=token)
        ok = result.get("success", result.get("ok"))
        detail = result.get("message") or result.get("error") or ""
        if ok or report:
            log("test     %-10s %s %s" % (name, "ok" if ok else "FAILED", detail))
        return bool(ok)
    except ApiError as e:
        if report:
            log("test     %-10s FAILED %s" % (name, e))
        return False


def find_profile(token):
    profile = find_by_name(token, "/v1.0/api/agent-profiles", PROFILE_NAME)
    if not profile:
        raise RuntimeError("The %s profile was not found; first boot should have created it." % PROFILE_NAME)
    return profile


def upsert_binding(token, profile_id, resource_id, spec):
    body = {
        "resourceId": resource_id,
        "alias": spec["name"],
        "accessMode": spec["access"],
        "enabled": True,
    }
    bindings = objects(call("GET", "/v1.0/api/agent-profiles/%s/bindings" % profile_id, token=token))
    existing = next((b for b in bindings if b.get("resourceId") == resource_id), None)
    if existing:
        call("PUT", "/v1.0/api/agent-profiles/%s/bindings/%s" % (profile_id, existing["id"]), body, token)
        log("binding  %-10s %s (updated)" % (spec["name"], spec["access"]))
    else:
        call("POST", "/v1.0/api/agent-profiles/%s/bindings" % profile_id, body, token)
        log("binding  %-10s %s (created)" % (spec["name"], spec["access"]))


def ollama_model():
    """Return (model, base URL) for a reachable Ollama, or (None, None)."""
    try:
        with urllib.request.urlopen(OLLAMA_URL + "/api/tags", timeout=5) as response:
            tags = json.loads(response.read().decode("utf-8"))
    except Exception:  # noqa: BLE001 (unreachable is the common case)
        return None, None
    names = [m.get("name", "") for m in tags.get("models", [])]
    chat = [n for n in names if "embed" not in n]
    if OLLAMA_MODEL:
        model = OLLAMA_MODEL
    elif chat:
        preferred = [n for n in chat if n.startswith(("llama3.1", "qwen", "llama3"))]
        model = (preferred or chat)[0]
    else:
        return None, None
    return model, (OLLAMA_MODEL_URL or OLLAMA_URL)


def seed_model_endpoint(token, profile):
    model, base_url = ollama_model()
    if not model:
        log("model    no Ollama answered at %s; no model endpoint created (add one in the dashboard)" % OLLAMA_URL)
        return None
    name = "factory-ollama"
    body = {"name": name, "provider": "Ollama", "baseUrl": base_url, "model": model, "timeoutSeconds": 180, "maxRetries": 0}
    existing = find_by_name(token, "/v1.0/api/model-endpoints", name)
    if existing:
        call("PUT", "/v1.0/api/model-endpoints/%s" % existing["id"], body, token)
        endpoint_id = existing["id"]
    else:
        endpoint_id = call("POST", "/v1.0/api/model-endpoints", body, token)["id"]
    if not profile.get("inferenceEndpointId"):
        call("PUT", "/v1.0/api/agent-profiles/%s" % profile["id"], {"inferenceEndpointId": endpoint_id}, token)
    log("model    %s (%s at %s) for the %s profile" % (name, model, base_url, PROFILE_NAME))
    return model


def read_state():
    try:
        with open(STATE_FILE, encoding="utf-8") as fh:
            return json.load(fh)
    except Exception:  # noqa: BLE001 (missing or unreadable state means "no saved credential")
        return None


def write_state(state):
    os.makedirs(os.path.dirname(STATE_FILE), exist_ok=True)
    with open(STATE_FILE, "w", encoding="utf-8") as fh:
        json.dump(state, fh)
    os.chmod(STATE_FILE, 0o600)


def seed_credential(token, profile_id):
    base = "/v1.0/api/tenants/%s/credentials" % TENANT
    body = {"name": CREDENTIAL_NAME, "agentProfileId": profile_id, "lockToProfile": True, "active": True}
    existing = find_by_name(token, base, CREDENTIAL_NAME)
    state = read_state()
    if existing:
        call("PUT", "%s/%s" % (base, existing["id"]), body, token)
        if state and state.get("id") == existing["id"] and state.get("accessKey") == existing.get("accessKey"):
            log("credential %s kept" % CREDENTIAL_NAME)
            return state
        # The secret is shown only once and the saved copy is gone: rotate to get a usable pair again.
        result = call("POST", "%s/%s/rotate" % (base, existing["id"]), token=token)
        log("credential %s rotated (no saved secret)" % CREDENTIAL_NAME)
    else:
        result = call("POST", base, body, token)
        log("credential %s created" % CREDENTIAL_NAME)
    state = {"id": result["credential"]["id"], "accessKey": result["accessKey"], "secretKey": result["secretKey"]}
    write_state(state)
    return state


def verify_credential(state):
    request = urllib.request.Request(HUGO_URL + "/v1.0/token/details")
    request.add_header("x-access-key", state["accessKey"])
    request.add_header("x-secret-key", state["secretKey"])
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            details = json.loads(response.read().decode("utf-8"))
            return details.get("credentialId") == state["id"]
    except urllib.error.HTTPError as e:
        log("credential check returned %s: %s" % (e.code, e.read().decode("utf-8", "replace")[:300]))
        return False


def main():
    wait_for_server()
    token = sign_in()

    with open(SSH_KEY_FILE, encoding="utf-8") as fh:
        private_key = fh.read()

    secrets = [
        ("factory-web-01-key", "PrivateKey", {"privateKey": private_key}, "SSH key of the hugo user on web-01 (generated per factory reset)"),
        ("factory-prod-db", "Password", {"username": "hugo", "password": "HugoTestbed1!"}, "PostgreSQL login for the shop database"),
        ("factory-legacy-db", "Password", {"username": "hugo", "password": "HugoTestbed1!"}, "MySQL login for the shop database"),
        ("factory-minio", "AccessKeyPair", {"accessKeyId": "factory-minio-demo", "secretAccessKey": "factory-minio-demo-secret"}, "MinIO root keys (demo only)"),
        ("factory-redis-reader", "Password", {"username": "hugo-reader", "password": "hugo-reader-password"}, "Redis ACL user limited to read commands"),
        ("factory-grafana", "Password", {"username": "admin", "password": "admin"}, "Grafana admin login of the factory stack"),
        ("factory-localstack", "AccessKeyPair", {"accessKeyId": "test", "secretAccessKey": "test"}, "LocalStack keys (LocalStack accepts any pair; demo only)"),
    ]
    secret_ids = {}
    for name, kind, values, description in secrets:
        secret_ids[name] = upsert_secret(token, name, kind, values, description)

    resources = [
        {
            "name": "prod-db", "type": "postgresql", "access": "ReadOnly",
            "description": "Shop database (PostgreSQL 16): customers, products, orders, order items.",
            "notes": "Demo copy of the shop schema. The orders table holds twelve orders; legacy_accounts joins legacy_invoices by naming convention only.",
            "settings": {"host": "postgres", "port": 5432, "database": "shop", "sslMode": "Disable"},
            "secrets": {"password": "factory-prod-db"},
        },
        {
            "name": "legacy-db", "type": "mysql", "access": "ReadOnly",
            "description": "Legacy shop database (MySQL 8.4) with the same schema as prod-db.",
            "notes": "Older system kept for reconciliation. Compare against prod-db before trusting either.",
            "settings": {"host": "mysql", "port": 3306, "database": "shop", "sslMode": "Disable"},
            "secrets": {"password": "factory-legacy-db"},
        },
        {
            "name": "web-01", "type": "ssh", "access": "ReadWriteWithApproval",
            "description": "Web server (OpenSSH). Application log at /srv/hugo/app.log.",
            "notes": "Commands that change the host pause for approval. sudo is disabled.",
            "settings": {"host": "ssh-target", "port": 2222, "username": "hugo", "authMethod": "PrivateKey"},
            "secrets": {"privateKey": "factory-web-01-key"},
        },
        {
            "name": "assets", "type": "s3", "access": "ReadOnly",
            "description": "Asset bucket hugo-testbed on MinIO (S3 API).",
            "settings": {"bucket": "hugo-testbed", "region": "us-east-1", "endpoint": "http://minio:9000", "useSsl": False},
            "secrets": {"accessKeyPair": "factory-minio"},
        },
        {
            "name": "cache", "type": "redis", "access": "ReadWriteWithApproval",
            "description": "Session and queue cache (Redis 7.4). Keys live under the hugo: prefix.",
            "notes": "Signed in as the read-only ACL user hugo-reader, so writes fail at the server even when approved.",
            "settings": {"endpoints": ["redis:6379"], "clusterMode": "Standalone"},
            "secrets": {"password": "factory-redis-reader"},
        },
        {
            "name": "metrics", "type": "prometheus", "access": "ReadOnly",
            "description": "Prometheus of this stack; it scrapes Hugo's own /metrics.",
            "settings": {"baseUrl": "http://hugo-prometheus:9090"},
        },
        {
            "name": "logs", "type": "loki", "access": "ReadOnly",
            "description": "Loki of this stack; it holds Hugo's own container logs (labels app, component, level).",
            "settings": {"baseUrl": "http://hugo-loki:3100"},
        },
        {
            "name": "grafana", "type": "http", "access": "ReadOnly",
            "description": "Grafana HTTP API of this stack (dashboards in the Hugo folder, datasources prometheus, tempo, loki).",
            "settings": {"baseUrl": "http://hugo-grafana:3000", "authType": "Basic", "allowedMethods": ["GET", "HEAD"], "allowedPathPrefixes": ["/api/"]},
            "secrets": {"basicAuth": "factory-grafana"},
        },
        {
            "name": "search", "type": "elasticsearch", "access": "ReadOnly",
            "description": "OpenSearch 2.19 log index hugo-logs-2026.10 (50 application log documents).",
            "notes": "Only hugo-logs-* is allowed. The index hugo-secret and the alias hugo-alias exist but are refused by the index allow list.",
            "settings": {"baseUrl": "http://opensearch:9200", "flavor": "OpenSearch", "allowedIndexPatterns": ["hugo-logs-*"]},
        },
        {
            "name": "cloud", "type": "aws", "access": "ReadOnly",
            "description": "AWS account 000000000000 in us-east-1, emulated by LocalStack: EC2, CloudWatch, CloudWatch Logs, SQS, SNS, Route 53.",
            "notes": "Three EC2 instances tagged env=testbed, alarm hugo-testbed-high-latency in ALARM, log group /hugo/testbed/app, queue hugo-testbed-orders with a dead-letter queue.",
            "settings": {
                "region": "us-east-1",
                "endpointOverride": "http://localstack:4566",
                "allowedServices": ["sts", "ec2", "cloudwatch", "logs", "sqs", "sns", "route53"],
            },
            "secrets": {"accessKeyPair": "factory-localstack"},
        },
    ]
    resource_ids = {}
    failures = []
    for spec in resources:
        resource_ids[spec["name"]] = upsert_resource(token, spec, secret_ids)
    for spec in resources:
        # A Loki with no log lines in the last hour fails the connection test ("Loki returned no data"), and a fresh
        # stack has none until Alloy ships Hugo's first lines. Give it 90 seconds, then warn instead of failing.
        is_loki = spec["type"] == "loki"
        if not test_resource(token, resource_ids[spec["name"]], spec["name"], 18 if is_loki else 3):
            if is_loki:
                log("warning  %s has no log lines yet; the test passes once Alloy ships Hugo's logs" % spec["name"])
            else:
                failures.append(spec["name"])

    profile = find_profile(token)
    for spec in resources:
        upsert_binding(token, profile["id"], resource_ids[spec["name"]], spec)

    model = seed_model_endpoint(token, profile)
    state = seed_credential(token, profile["id"])
    if not verify_credential(state):
        failures.append("credential")

    print("", flush=True)
    print("Hugo factory stack is seeded.", flush=True)
    print("  Dashboard   %s   tenant %s, %s / %s" % (DASHBOARD_URL, TENANT, EMAIL, DEMO_PASSWORD), flush=True)
    print("  API and MCP %s   (MCP at %s/mcp)" % (PUBLIC_URL, PUBLIC_URL), flush=True)
    print("  Grafana     %s   admin / admin" % GRAFANA_URL, flush=True)
    print("  Profile     %s with %d bindings: %s" % (PROFILE_NAME, len(resources), ", ".join("%s (%s)" % (r["name"], r["access"]) for r in resources)), flush=True)
    print("  Model       %s" % (("Ollama " + model) if model else "none (no Ollama found; add an endpoint in the dashboard to chat)"), flush=True)
    print("  Demo credential %s (bound and locked to %s; demo only, never for production)" % (CREDENTIAL_NAME, PROFILE_NAME), flush=True)
    print("    HUGO_ACCESS_KEY=%s" % state["accessKey"], flush=True)
    print("    HUGO_SECRET_KEY=%s" % state["secretKey"], flush=True)
    print("  Claude Code:", flush=True)
    print("    claude mcp add --transport http hugo-factory %s/mcp --header \"x-access-key: %s\" --header \"x-secret-key: %s\""
          % (PUBLIC_URL, state["accessKey"], state["secretKey"]), flush=True)
    if failures:
        print("", flush=True)
        print("Seed finished with failures: %s" % ", ".join(failures), flush=True)
        return 1
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as e:  # noqa: BLE001 (report and fail the one-shot container)
        print("[seed] FAILED: %s" % e, flush=True)
        sys.exit(1)
