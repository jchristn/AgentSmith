#!/usr/bin/env bash
# Smoke test of a deployed Hugo, run from your machine through temporary port-forwards (it never touches the
# ports 8800, 8801, and 3000 that port-forward.sh uses). Exit code 0 means every check passed:
#
#   1. the server and dashboard pods are Ready, and the server runs exactly one replica;
#   2. GET /v1.0/api/health answers 200 on the server, and through the dashboard's proxy;
#   3. the dashboard serves the console (index.html) and /health;
#   4. sign-in as the seeded tenant user works, and the session can call the API right away (a pending password
#      change is advisory and never blocks a route);
#   5. GET /v1.0/api/connectors lists the connector catalog;
#   6. the compiled instructions of the default profile Hugo introduce Agent Smith;
#   7. with the observability component: Grafana is healthy and its Hugo folder holds the Hugo dashboards, and
#      Prometheus has scraped the server.
#
# Environment:
#   HUGO_SMOKE_EMAIL          user to sign in as (default anderson@hugo.local, tenant default)
#   HUGO_SMOKE_PASSWORD       that user's current password (default: password, the seeded value)
# Needs kubectl, curl, and python3.
source "$(dirname "$0")/common.sh"
need kubectl
need curl
need python3

EMAIL="${HUGO_SMOKE_EMAIL:-anderson@hugo.local}"
PASSWORD="${HUGO_SMOKE_PASSWORD:-password}"
TENANT="default"
REST_PORT="${SMOKE_REST_PORT:-18800}"
DASHBOARD_PORT="${SMOKE_DASHBOARD_PORT:-18801}"
GRAFANA_PORT="${SMOKE_GRAFANA_PORT:-13000}"
PROMETHEUS_PORT="${SMOKE_PROMETHEUS_PORT:-19090}"
PASSED=0

pass() { PASSED=$((PASSED + 1)); log "PASS $*"; }
die() { log "FAIL $*"; exit 1; }
json() { python3 -c 'import json,sys; d=json.load(sys.stdin); print(eval(sys.argv[1], {"d": d}))' "$1"; }

cleanup() { [ -n "${PIDS}" ] && kill ${PIDS} 2>/dev/null; return 0; }
trap cleanup EXIT INT TERM
forward() { start_forward "$1" "$2"; }
wait_port() {
  local i
  for i in $(seq 1 40); do
    curl -s -o /dev/null "http://127.0.0.1:$1/" && return 0
    sleep 0.5
  done
  die "port-forward to 127.0.0.1:$1 did not come up"
}

# 1. Pods.
kc wait --for=condition=Ready pod -l app.kubernetes.io/name=hugo-server --timeout=120s >/dev/null || die "the server pod is not Ready"
kc wait --for=condition=Ready pod -l app.kubernetes.io/name=hugo-dashboard --timeout=120s >/dev/null || die "the dashboard pod is not Ready"
REPLICAS="$(kc get deployment hugo-server -o jsonpath='{.spec.replicas}')"
STRATEGY="$(kc get deployment hugo-server -o jsonpath='{.spec.strategy.type}')"
[ "${REPLICAS}" = "1" ] && [ "${STRATEGY}" = "Recreate" ] || die "the server must run 1 replica with Recreate (found ${REPLICAS}, ${STRATEGY})"
pass "pods Ready; server runs 1 replica with the Recreate strategy"

forward svc/hugo-server "${REST_PORT}:8800"
forward svc/hugo-dashboard "${DASHBOARD_PORT}:80"
wait_port "${REST_PORT}"
wait_port "${DASHBOARD_PORT}"
API="http://127.0.0.1:${REST_PORT}"
UI="http://127.0.0.1:${DASHBOARD_PORT}"

# 2 and 3. Health and the console.
HEALTH="$(curl -sf "${API}/v1.0/api/health")" || die "GET /v1.0/api/health on the server"
pass "server health: ${HEALTH}"
curl -sf -o /dev/null "${UI}/v1.0/api/health" || die "GET /v1.0/api/health through the dashboard proxy"
pass "server health through the dashboard proxy"
curl -sf "${UI}/health" >/dev/null || die "GET /health on the dashboard"
curl -sf "${UI}/" | grep -qi '<div id="root"' || die "the dashboard did not serve the console"
pass "dashboard serves /health and the console"

# 4. Sign in. The password change is advisory, so the session works at once.
login() {
  curl -sf -X POST "${API}/v1.0/token" -H 'Content-Type: application/json' \
    -d "{\"tenantId\":\"${TENANT}\",\"email\":\"${EMAIL}\",\"password\":\"$1\"}" | json 'd["token"]'
}
TOKEN="$(login "${PASSWORD}")" || die "sign-in as ${EMAIL} (set HUGO_SMOKE_PASSWORD to the current password)"
pass "signed in as ${EMAIL}"
CODE="$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer ${TOKEN}" "${API}/v1.0/api/connectors")"
[ "${CODE}" = "200" ] || die "unexpected status ${CODE} from /v1.0/api/connectors after sign-in"
pass "the session calls the API without a forced password change"
AUTH=(-H "Authorization: Bearer ${TOKEN}")

# 5. Connectors.
COUNT="$(curl -sf "${AUTH[@]}" "${API}/v1.0/api/connectors" | json 'len(d)')" || die "list connectors"
[ "${COUNT}" -gt 0 ] || die "the connector catalog is empty"
pass "connector catalog lists ${COUNT} connectors"

# 6. Compiled instructions of profile Hugo.
PROFILE="$(curl -sf "${AUTH[@]}" "${API}/v1.0/api/agent-profiles" | json '[p["id"] for p in d["objects"] if p["name"]=="Hugo"][0]')" \
  || die "find agent profile Hugo"
curl -sf "${AUTH[@]}" "${API}/v1.0/api/agent-profiles/${PROFILE}/compiled-instructions" | grep -q "Agent Smith" \
  || die "the compiled instructions of profile Hugo do not mention Agent Smith"
pass "compiled instructions of profile Hugo (${PROFILE}) introduce Agent Smith"

# 7. Observability.
if kc get service hugo-grafana >/dev/null 2>&1; then
  forward svc/hugo-grafana "${GRAFANA_PORT}:3000"
  forward svc/hugo-prometheus "${PROMETHEUS_PORT}:9090"
  wait_port "${GRAFANA_PORT}"
  wait_port "${PROMETHEUS_PORT}"
  GF_PASSWORD="$(kc get secret hugo-secrets -o jsonpath='{.data.grafana-admin-password}' | base64 --decode)"
  curl -sf "http://127.0.0.1:${GRAFANA_PORT}/api/health" >/dev/null || die "Grafana health"
  DASHBOARDS="$(curl -sf -u "admin:${GF_PASSWORD}" "http://127.0.0.1:${GRAFANA_PORT}/api/search?folderUIDs=hugo&type=dash-db" | json 'len(d)')" \
    || die "Grafana search in folder Hugo"
  EXPECTED="$(ls "${REPO_DIR}/k8s/components/observability/dashboards/"*.json | wc -l | tr -d ' ')"
  [ "${DASHBOARDS}" = "${EXPECTED}" ] || die "Grafana folder Hugo holds ${DASHBOARDS} dashboards, expected ${EXPECTED}"
  pass "Grafana folder Hugo holds ${DASHBOARDS} dashboards"
  UP=""
  for i in $(seq 1 12); do
    UP="$(curl -sf "http://127.0.0.1:${PROMETHEUS_PORT}/api/v1/query?query=up%7Bjob%3D%22hugo-server%22%7D" | json 'd["data"]["result"][0]["value"][1] if d["data"]["result"] else ""')" || true
    [ "${UP}" = "1" ] && break
    sleep 5
  done
  [ "${UP}" = "1" ] || die "Prometheus has no successful scrape of job hugo-server yet (up != 1)"
  pass "Prometheus scrapes the server (up{job=\"hugo-server\"} = 1)"
fi

log "smoke test passed (${PASSED} checks, context $(context_name))"
