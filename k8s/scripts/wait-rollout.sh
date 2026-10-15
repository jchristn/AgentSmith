#!/usr/bin/env bash
# Waits for PostgreSQL (when deployed), the server, the dashboard, and the observability stack (when deployed),
# then prints pod status. Exit code 0 means everything rolled out and passed its readiness probes.
source "$(dirname "$0")/common.sh"
need kubectl
wait_for_rollout
kc get pods -o wide
