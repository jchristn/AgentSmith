#!/usr/bin/env bash
# Forwards Hugo in minikube to 127.0.0.1: dashboard 8801, REST, MCP, and A2A 8800, and with the observability
# component Grafana 3000 (plus Prometheus 9090, Loki 3100, Tempo 3200). Ctrl+C stops them.
# The same port variables as k8s/scripts/port-forward.sh apply (REST_PORT, DASHBOARD_PORT, GRAFANA_PORT, ...).
source "$(dirname "$0")/common.sh"
require_running
exec "${K8S_SCRIPTS}/port-forward.sh"
