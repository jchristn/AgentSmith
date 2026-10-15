#!/usr/bin/env bash
# Smoke test of Hugo in minikube: health, the console, sign-in with the forced password change, the connector
# catalog, the compiled instructions of profile Hugo, and (with observability) Grafana's Hugo folder and the
# Prometheus scrape. See k8s/scripts/smoke-test.sh for the environment variables.
source "$(dirname "$0")/common.sh"
require_running
exec "${K8S_SCRIPTS}/smoke-test.sh"
