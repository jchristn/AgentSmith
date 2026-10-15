#!/usr/bin/env bash
# Telemetry verifier (plan task P10.20). Drives a small workload through the Hugo REST API, waits for Prometheus to
# scrape the server, then checks that every expected metric family exists in Prometheus.
#
# Expected families come from the metric catalog: the `hugo_*` names in docs/TELEMETRY.md (generated from
# HugoMetricCatalog), plus a few Watson and runtime families the dashboards depend on. Families are classified as:
#
#   required     catalog families the workload always produces, and the Watson/runtime families. Missing => exit 1.
#   conditional  catalog families that only appear after an event this workload does not cause (a database error,
#                a secret reveal, an agent turn against a real model, ...). Missing => warning only.
#   unemitted    catalog families with no call site in the server yet (a known gap, see KNOWN_UNEMITTED).
#                Missing => warning only; remove the entry once the server records the family.
#   pending      plan section 21 families not in the catalog (being added, or emitted outside the catalog such as
#                the agent and model families). Missing => warning only. Once a pending family lands in the
#                catalog it is checked as required unless it is also conditional.
#
# Usage:
#   scripts/verify-telemetry.sh [options]
#
# Options (each also settable through the environment variable in brackets):
#   --base-url URL         Hugo REST base URL [HUGO_BASE_URL] (default http://127.0.0.1:8800)
#   --prometheus-url URL   Prometheus base URL [HUGO_PROMETHEUS_URL] (default http://127.0.0.1:9090)
#   --job NAME             Prometheus scrape job of the server [HUGO_PROMETHEUS_JOB] (default hugo-server)
#   --tenant ID            Tenant to sign in to [HUGO_TENANT] (default default)
#   --email EMAIL          User to sign in as [HUGO_EMAIL] (default anderson@hugo.local)
#   --password PASSWORD    Current password [HUGO_PASSWORD] (default password, the seeded value)
#   --new-password PASS    When the server answers mustChangePassword (an advisory flag on seeded and reset
#                          accounts, D-048), change the password to PASS through POST /v1.0/api/users/me/password and
#                          sign in again [HUGO_NEW_PASSWORD]. When the current password is rejected, PASS is also
#                          tried, so the same command works on a second run. Optional: the authenticated reads run
#                          either way, because the flag blocks no route.
#   --iterations N         Times to repeat the authenticated reads (default 3)
#   --scrape-timeout S     Seconds to wait for a Prometheus scrape after the workload (default 60)
#   --catalog FILE         Catalog file to read hugo_* families from (default <repo>/docs/TELEMETRY.md)
#   -h, --help             Show this help
#
# Needs bash, curl, and python3. Exit code 0 when every required family exists, 1 when one is missing, 2 on a
# usage or connectivity error.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BASE_URL="${HUGO_BASE_URL:-http://127.0.0.1:8800}"
PROM_URL="${HUGO_PROMETHEUS_URL:-http://127.0.0.1:9090}"
JOB="${HUGO_PROMETHEUS_JOB:-hugo-server}"
TENANT="${HUGO_TENANT:-default}"
EMAIL="${HUGO_EMAIL:-anderson@hugo.local}"
PASSWORD="${HUGO_PASSWORD:-password}"
NEW_PASSWORD="${HUGO_NEW_PASSWORD:-}"
ITERATIONS=3
SCRAPE_TIMEOUT=60
CATALOG="$SCRIPT_DIR/../docs/TELEMETRY.md"

# Watson and .NET runtime families the dashboards query. Always required.
RUNTIME_REQUIRED=(
  http_server_request_duration_seconds
  http_server_active_requests
  watson_server_up
  watson_server_connections_active
  process_uptime_seconds
  process_memory_usage_bytes
  dotnet_process_cpu_time_seconds_total
)

# Catalog (and future catalog) families that need an event this workload does not cause. Warn when missing.
CONDITIONAL=(
  hugo_model_connector_requests_total
  hugo_model_connector_tokens_total
  hugo_auth_rate_limited_total
  hugo_db_errors_total
  hugo_db_transaction_retries_total
  hugo_retention_deleted_total
  hugo_background_duration_seconds
  hugo_background_iterations_total
  hugo_background_jobs_total
  hugo_background_job_duration_seconds
  hugo_outbound_requests_total
  hugo_outbound_duration_seconds
  hugo_observability_reachable
  hugo_telemetry_labels_folded_total
  hugo_telemetry_diagnostics_total
  hugo_secret_operations_total
  hugo_secret_redactions_total
  hugo_sse_streams_active
  hugo_sse_events_total
  hugo_audit_events_total
  hugo_authz_denials_total
  hugo_agent_turns_total
  hugo_agent_turn_duration_seconds
  hugo_agent_stage_duration_seconds
  hugo_agent_iterations
  hugo_model_requests_total
  hugo_model_request_duration_seconds
  hugo_model_time_to_first_token_seconds
  hugo_model_tokens_total
  hugo_tool_invocations_total
  hugo_tool_duration_seconds
  hugo_tool_output_bytes
  hugo_connector_sessions_active
  hugo_approvals_total
  hugo_approval_wait_seconds
  hugo_mcp_requests_total
  hugo_a2a_requests_total
  hugo_instructions_compile_duration_seconds
  hugo_resource_health
)

# Catalog families that nothing records yet. Warn when missing (and say so when one starts appearing).
KNOWN_UNEMITTED=(
  hugo_authz_requests_total
)

# Plan section 21 families. Those not in the catalog yet are reported as pending (warning only).
SECTION21=(
  hugo_tool_invocations_total
  hugo_tool_duration_seconds
  hugo_tool_output_bytes
  hugo_connector_sessions_active
  hugo_approvals_total
  hugo_approval_wait_seconds
  hugo_agent_turns_total
  hugo_agent_turn_duration_seconds
  hugo_agent_stage_duration_seconds
  hugo_agent_iterations
  hugo_model_requests_total
  hugo_model_request_duration_seconds
  hugo_model_time_to_first_token_seconds
  hugo_model_tokens_total
  hugo_mcp_requests_total
  hugo_a2a_requests_total
  hugo_instructions_compile_duration_seconds
  hugo_secret_operations_total
  hugo_secret_redactions_total
  hugo_auth_requests_total
  hugo_authz_requests_total
  hugo_background_jobs_total
  hugo_background_job_duration_seconds
  hugo_resource_health
)

usage() {
  awk 'NR > 1 && !/^#/ { exit } NR > 1' "$0" | sed 's/^# \{0,1\}//'
}

while [ $# -gt 0 ]; do
  case "$1" in
    --base-url) BASE_URL="$2"; shift 2 ;;
    --prometheus-url) PROM_URL="$2"; shift 2 ;;
    --job) JOB="$2"; shift 2 ;;
    --tenant) TENANT="$2"; shift 2 ;;
    --email) EMAIL="$2"; shift 2 ;;
    --password) PASSWORD="$2"; shift 2 ;;
    --new-password) NEW_PASSWORD="$2"; shift 2 ;;
    --iterations) ITERATIONS="$2"; shift 2 ;;
    --scrape-timeout) SCRAPE_TIMEOUT="$2"; shift 2 ;;
    --catalog) CATALOG="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1 (see --help)" >&2; exit 2 ;;
  esac
done

BASE_URL="${BASE_URL%/}"
PROM_URL="${PROM_URL%/}"
WARNINGS=0

log() { printf '%s\n' "$*"; }
warn() { printf 'WARN %s\n' "$*"; WARNINGS=$((WARNINGS + 1)); }

for tool in curl python3; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "verify-telemetry: $tool is required" >&2
    exit 2
  fi
done

# json_field <field>: reads JSON on stdin and prints a top-level field (empty when absent or not JSON).
json_field() {
  python3 -c 'import json,sys
try:
    v = json.load(sys.stdin).get(sys.argv[1], "")
except Exception:
    v = ""
print(str(v).lower() if isinstance(v, bool) else v)' "$1"
}

json_body() {
  python3 -c 'import json,sys; print(json.dumps(dict(zip(sys.argv[1::2], sys.argv[2::2]))))' "$@"
}

# call <method> <path> [token] [json body]: prints the status code; the body goes to $RESPONSE_FILE.
RESPONSE_FILE="$(mktemp)"
trap 'rm -f "$RESPONSE_FILE"' EXIT
call() {
  local method="$1" path="$2" token="${3:-}" body="${4:-}"
  local args=(-s -o "$RESPONSE_FILE" -w '%{http_code}' -X "$method" --max-time 15)
  [ -n "$token" ] && args+=(-H "Authorization: Bearer $token")
  [ -n "$body" ] && args+=(-H 'Content-Type: application/json' --data "$body")
  curl "${args[@]}" "$BASE_URL$path" || true
}

issue_token() {
  local password="$1"
  local status
  status=$(call POST /v1.0/token "" "$(json_body tenantId "$TENANT" email "$EMAIL" password "$password")")
  if [ "$status" = "200" ]; then
    TOKEN=$(json_field token < "$RESPONSE_FILE")
    MUST_CHANGE=$(json_field mustChangePassword < "$RESPONSE_FILE")
    return 0
  fi
  return 1
}

# ------------------------------------------------------------------ workload

log "Hugo:       $BASE_URL"
log "Prometheus: $PROM_URL (job $JOB)"

status=$(call GET /v1.0/api/health)
if [ "$status" != "200" ]; then
  echo "verify-telemetry: $BASE_URL/v1.0/api/health answered '$status'; is the server running?" >&2
  exit 2
fi
if ! curl -sf --max-time 10 "$PROM_URL/-/ready" >/dev/null; then
  echo "verify-telemetry: Prometheus at $PROM_URL is not ready" >&2
  exit 2
fi

log "Workload: anonymous routes"
for path in /v1.0/api/health /v1.0/api/health/live /v1.0/api/health/ready /v1.0/api/info; do
  log "  GET $path -> $(call GET "$path")"
done

log "Workload: rejected requests (authentication failures)"
log "  POST /v1.0/token (wrong password) -> $(call POST /v1.0/token "" "$(json_body tenantId "$TENANT" email "$EMAIL" password "verify-telemetry-wrong-password")")"
log "  GET /v1.0/api/users/me (no token) -> $(call GET /v1.0/api/users/me)"
log "  GET /v1.0/api/users/me (bad token) -> $(call GET /v1.0/api/users/me "verify-telemetry-invalid-token")"

log "Workload: sign in as $EMAIL (tenant $TENANT)"
TOKEN=""
MUST_CHANGE=""
if issue_token "$PASSWORD"; then
  log "  POST /v1.0/token -> 200 (mustChangePassword=$MUST_CHANGE)"
elif [ -n "$NEW_PASSWORD" ] && issue_token "$NEW_PASSWORD"; then
  PASSWORD="$NEW_PASSWORD"
  log "  POST /v1.0/token with the new password -> 200 (mustChangePassword=$MUST_CHANGE)"
else
  warn "sign-in failed for $EMAIL; authenticated reads skipped"
fi

if [ -n "$TOKEN" ] && [ "$MUST_CHANGE" = "true" ]; then
  if [ -n "$NEW_PASSWORD" ] && [ "$NEW_PASSWORD" != "$PASSWORD" ]; then
    status=$(call POST /v1.0/api/users/me/password "$TOKEN" "$(json_body currentPassword "$PASSWORD" newPassword "$NEW_PASSWORD")")
    log "  POST /v1.0/api/users/me/password -> $status"
    if [ "$status" = "200" ] && issue_token "$NEW_PASSWORD"; then
      PASSWORD="$NEW_PASSWORD"
      log "  POST /v1.0/token with the new password -> 200 (mustChangePassword=$MUST_CHANGE)"
    else
      warn "password change failed ($status): $(head -c 300 "$RESPONSE_FILE")"
    fi
  fi
  if [ "$MUST_CHANGE" = "true" ]; then
    # Advisory only (D-048): no route is blocked, so the reads below still run.
    log "  $EMAIL still has a seeded or reset password (advisory; pass --new-password to change it)"
  fi
fi

if [ -n "$TOKEN" ]; then
  log "Workload: authenticated reads x$ITERATIONS (403 is expected for system administrator routes)"
  for _ in $(seq 1 "$ITERATIONS"); do
    for path in /v1.0/api/users/me /v1.0/token/details /v1.0/api/server-info /v1.0/api/observability/status \
                "/v1.0/api/request-history?maxResults=5" /v1.0/api/request-history/summary \
                /v1.0/api/settings /v1.0/api/tenants /v1.0/api/system/master-key; do
      log "  GET $path -> $(call GET "$path" "$TOKEN")"
    done
  done
  # A short-lived server-sent events stream, so the SSE families have something to count.
  curl -s -o /dev/null --max-time 3 -H "Authorization: Bearer $TOKEN" -H 'Accept: text/event-stream' \
    "$BASE_URL/v1.0/api/events" || true
  log "  GET /v1.0/api/events (3 second stream) -> closed"
  log "  POST /v1.0/token/refresh -> $(call POST /v1.0/token/refresh "$TOKEN")"
fi

WORKLOAD_END=$(python3 -c 'import time; print(time.time())')

# ------------------------------------------------------------------ wait for a scrape

log "Waiting for Prometheus to scrape $JOB after the workload (up to ${SCRAPE_TIMEOUT}s)"
deadline=$(( $(date +%s) + SCRAPE_TIMEOUT ))
scraped=""
while [ "$(date +%s)" -lt "$deadline" ]; do
  scraped=$(curl -s --max-time 10 "$PROM_URL/api/v1/targets?state=active" | python3 -c '
import datetime, json, sys
job, end = sys.argv[1], float(sys.argv[2])
try:
    targets = json.load(sys.stdin)["data"]["activeTargets"]
except Exception:
    sys.exit(0)
for t in targets:
    if t.get("labels", {}).get("job") != job or t.get("health") != "up":
        continue
    stamp = t.get("lastScrape", "")
    # RFC 3339 with nanoseconds: trim to microseconds for fromisoformat.
    head, _, frac = stamp.partition(".")
    zone = frac[frac.find("+") if "+" in frac else frac.find("Z"):] if frac else ""
    digits = frac[: len(frac) - len(zone)][:6] if frac else "0"
    zone = "+00:00" if zone in ("Z", "") else zone
    try:
        when = datetime.datetime.fromisoformat("%s.%s%s" % (head, digits.ljust(6, "0"), zone)).timestamp()
    except ValueError:
        continue
    if when > end:
        print("yes")
        break
' "$JOB" "$WORKLOAD_END")
  [ "$scraped" = "yes" ] && break
  sleep 2
done
if [ "$scraped" != "yes" ]; then
  echo "verify-telemetry: Prometheus did not scrape job $JOB within ${SCRAPE_TIMEOUT}s (is the target up?)" >&2
  exit 1
fi
log "  scraped"

# ------------------------------------------------------------------ check families

if [ ! -f "$CATALOG" ]; then
  echo "verify-telemetry: catalog $CATALOG not found (use --catalog)" >&2
  exit 2
fi

NAMES_JSON=$(curl -s --max-time 15 -G "$PROM_URL/api/v1/label/__name__/values" --data-urlencode "match[]={job=\"$JOB\"}")

python3 - "$CATALOG" "$NAMES_JSON" "${#RUNTIME_REQUIRED[@]}" "${RUNTIME_REQUIRED[@]}" \
  "${#CONDITIONAL[@]}" "${CONDITIONAL[@]}" "${#KNOWN_UNEMITTED[@]}" "${KNOWN_UNEMITTED[@]}" \
  "${#SECTION21[@]}" "${SECTION21[@]}" <<'PY'
import json
import re
import sys

args = sys.argv[1:]
catalog_path, names_json = args[0], args[1]
rest = args[2:]
def take(rest):
    n = int(rest[0])
    return rest[1:1 + n], rest[1 + n:]
runtime, rest = take(rest)
conditional, rest = take(rest)
unemitted, rest = take(rest)
section21, rest = take(rest)

try:
    names = set(json.loads(names_json)["data"])
except Exception:
    print("FAIL could not read metric names from Prometheus", file=sys.stderr)
    sys.exit(2)

catalog = []
with open(catalog_path, encoding="utf-8") as fh:
    for line in fh:
        m = re.match(r"^\| `(hugo_[a-z0-9_]+)` \|", line)
        if m and m.group(1) not in catalog:
            catalog.append(m.group(1))
if not catalog:
    print("FAIL no hugo_* families found in %s" % catalog_path, file=sys.stderr)
    sys.exit(2)

def present(family):
    return any(n in names for n in (family, family + "_bucket", family + "_count"))

required = [f for f in catalog if f not in conditional and f not in unemitted] + list(runtime)
cond = [f for f in catalog if f in conditional]
gaps = [f for f in catalog if f in unemitted and f not in conditional]
pending = [f for f in section21 if f not in catalog]

missing = []
print()
print("Required families (%d):" % len(required))
for f in required:
    ok = present(f)
    print("  %-4s %s" % ("ok" if ok else "MISS", f))
    if not ok:
        missing.append(f)

warnings = 0
print()
print("Conditional catalog families (%d, warning only):" % len(cond))
for f in cond:
    ok = present(f)
    warnings += 0 if ok else 1
    print("  %-4s %s" % ("ok" if ok else "warn", f))

print()
print("Catalog families nothing records yet (%d, warning only):" % len(gaps))
for f in gaps:
    ok = present(f)
    warnings += 0 if ok else 1
    print("  %-4s %s%s" % ("ok" if ok else "warn", f, "  (now emitted: remove it from KNOWN_UNEMITTED)" if ok else ""))

print()
print("Section 21 families not in the catalog (%d, warning only):" % len(pending))
for f in pending:
    ok = present(f)
    warnings += 0 if ok else 1
    print("  %-4s %s" % ("ok" if ok else "warn", f))

print()
if missing:
    print("FAIL %d required metric families missing: %s" % (len(missing), ", ".join(missing)))
    sys.exit(1)
print("PASS %d required families present (%d conditional or pending families absent, see warn lines)" % (len(required), warnings))
PY
rc=$?
if [ "$WARNINGS" -gt 0 ]; then
  log "$WARNINGS workload warning(s), see WARN lines above"
fi
exit $rc
