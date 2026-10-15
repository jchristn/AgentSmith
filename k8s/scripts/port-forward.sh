#!/usr/bin/env bash
# Forwards Hugo to 127.0.0.1 on the same ports as Docker Compose: REST, MCP, and A2A on 8800 and the dashboard on
# 8801. With the observability component it also forwards Grafana (3000), Prometheus (9090), Loki (3100), and Tempo
# (3200). Ctrl+C stops them all.
# Pick other local ports when these are taken (the dashboard's external services card assumes the defaults):
#   REST_PORT=9800 DASHBOARD_PORT=9801 GRAFANA_PORT=3001 PROMETHEUS_PORT=9091 LOKI_PORT=3101 TEMPO_PORT=3201
source "$(dirname "$0")/common.sh"
need kubectl
REST_PORT="${REST_PORT:-8800}"
DASHBOARD_PORT="${DASHBOARD_PORT:-8801}"
GRAFANA_PORT="${GRAFANA_PORT:-3000}"
PROMETHEUS_PORT="${PROMETHEUS_PORT:-9090}"
LOKI_PORT="${LOKI_PORT:-3100}"
TEMPO_PORT="${TEMPO_PORT:-3200}"

OBSERVABILITY=0
kc get service hugo-grafana >/dev/null 2>&1 && OBSERVABILITY=1

PORTS="${REST_PORT} ${DASHBOARD_PORT}"
[ "${OBSERVABILITY}" = "1" ] && PORTS="${PORTS} ${GRAFANA_PORT} ${PROMETHEUS_PORT} ${LOKI_PORT} ${TEMPO_PORT}"
for port in ${PORTS}; do
  if (exec 3<>"/dev/tcp/127.0.0.1/${port}") 2>/dev/null; then
    fail "127.0.0.1:${port} is already in use (Docker Compose running?); choose free ports with REST_PORT, DASHBOARD_PORT, GRAFANA_PORT, PROMETHEUS_PORT, LOKI_PORT, and TEMPO_PORT"
  fi
done

forward() { start_forward "$@"; }
trap 'kill ${PIDS} 2>/dev/null' EXIT INT TERM

forward svc/hugo-server "${REST_PORT}:8800"
forward svc/hugo-dashboard "${DASHBOARD_PORT}:80"
log "dashboard http://127.0.0.1:${DASHBOARD_PORT}   REST http://127.0.0.1:${REST_PORT}   MCP http://127.0.0.1:${REST_PORT}/mcp"
if [ "${OBSERVABILITY}" = "1" ]; then
  forward svc/hugo-grafana "${GRAFANA_PORT}:3000"
  forward svc/hugo-prometheus "${PROMETHEUS_PORT}:9090"
  forward svc/hugo-loki "${LOKI_PORT}:3100"
  forward svc/hugo-tempo "${TEMPO_PORT}:3200"
  log "Grafana http://127.0.0.1:${GRAFANA_PORT} (admin)   Prometheus http://127.0.0.1:${PROMETHEUS_PORT}   Loki http://127.0.0.1:${LOKI_PORT}   Tempo http://127.0.0.1:${TEMPO_PORT}"
fi
log "Ctrl+C to stop"
wait
